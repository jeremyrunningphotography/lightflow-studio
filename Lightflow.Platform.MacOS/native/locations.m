#import <Foundation/Foundation.h>
#include <sys/mount.h>
#include <stdlib.h>
#include <string.h>

// Read-only mount snapshots, separate from Catalog storage admission. MNT_NOWAIT
// reads the kernel table without forcing I/O to an offline server.
char *lf_browser_mounts(void) {
    @autoreleasepool {
        int capacity = getfsstat(NULL, 0, MNT_NOWAIT) + 16;
        if (capacity <= 16) return NULL;
        struct statfs *table = calloc((size_t)capacity, sizeof(struct statfs));
        if (!table) return NULL;
        int count = getfsstat(table, capacity * (int)sizeof(struct statfs), MNT_NOWAIT);
        if (count < 0) { free(table); return NULL; }
        // Own the snapshot so concurrent refreshes never share libc's static buffer.
        NSData *snapshot = [NSData dataWithBytesNoCopy:table length:sizeof(struct statfs) * count freeWhenDone:YES];
        const struct statfs *mounts = snapshot.bytes;
        NSMutableArray *result = [NSMutableArray new];
        for (int i = 0; i < count; i++) {
            NSString *path = @(mounts[i].f_mntonname);
            NSURL *url = [NSURL fileURLWithPath:path isDirectory:YES];
            if (![path isEqualToString:@"/"] && ![path hasPrefix:@"/Volumes/"]) continue;
            NSArray *keys = @[NSURLVolumeLocalizedNameKey, NSURLVolumeUUIDStringKey,
                NSURLVolumeIsInternalKey, NSURLVolumeIsBrowsableKey, NSURLIsSymbolicLinkKey, NSURLIsHiddenKey];
            NSDictionary *facts = [url resourceValuesForKeys:keys error:nil] ?: @{};
            if (![path isEqualToString:@"/"] && ([facts[NSURLIsSymbolicLinkKey] boolValue] ||
                [facts[NSURLIsHiddenKey] boolValue] ||
                (facts[NSURLVolumeIsBrowsableKey] && ![facts[NSURLVolumeIsBrowsableKey] boolValue]))) continue;
            [result addObject:@{@"Path":path, @"Name":facts[NSURLVolumeLocalizedNameKey] ?: @"",
                @"VolumeId":facts[NSURLVolumeUUIDStringKey] ?: @"",
                @"Source":@(mounts[i].f_mntfromname), @"FileSystem":@(mounts[i].f_fstypename),
                @"Local":((mounts[i].f_flags & MNT_LOCAL) ? @YES : @NO),
                @"Internal":(facts[NSURLVolumeIsInternalKey] ? ([facts[NSURLVolumeIsInternalKey] boolValue] ? @YES : @NO) : [NSNull null])}];
        }
        NSData *json = [NSJSONSerialization dataWithJSONObject:result options:0 error:nil];
        if (!json) return NULL;
        return strdup([[NSString alloc] initWithData:json encoding:NSUTF8StringEncoding].UTF8String);
    }
}

#import <AppKit/AppKit.h>
typedef void (*LFVolumeCallback)(void);
@interface LFVolumeObserver : NSObject
@property(nonatomic, strong) NSArray *tokens;
@property(nonatomic, strong) NSNotificationCenter *center;
@property(nonatomic) LFVolumeCallback callback;
@property(nonatomic) BOOL active;
@end
@implementation LFVolumeObserver
@end

void *lf_browser_observe_volumes(LFVolumeCallback callback) {
    @autoreleasepool {
        if (!callback) return NULL;
        LFVolumeObserver *observer = [LFVolumeObserver new];
        observer.center = NSWorkspace.sharedWorkspace.notificationCenter;
        observer.callback = callback; observer.active = YES;
        NSMutableArray *tokens = [NSMutableArray new];
        __weak LFVolumeObserver *weakObserver = observer;
        for (NSNotificationName name in @[NSWorkspaceDidMountNotification,
            NSWorkspaceDidUnmountNotification, NSWorkspaceDidRenameVolumeNotification]) {
            id token = [observer.center addObserverForName:name object:nil queue:nil usingBlock:^(__unused NSNotification *note) {
                LFVolumeObserver *current = weakObserver;
                if (!current) return;
                @synchronized (current) {
                    if (current.active) current.callback();
                }
            }];
            [tokens addObject:token];
        }
        observer.tokens = tokens;
        return (__bridge_retained void *)observer;
    }
}
void lf_browser_stop_observing_volumes(void *handle) {
    @autoreleasepool {
        if (!handle) return;
        LFVolumeObserver *observer = (__bridge_transfer LFVolumeObserver *)handle;
        // Synchronize with in-flight callbacks before releasing the managed delegate.
        @synchronized (observer) {
            observer.active = NO;
        }
        for (id token in observer.tokens) [observer.center removeObserver:token];
        observer.tokens = nil;
    }
}
