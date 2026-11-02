#import <AppKit/AppKit.h>
// Test-only synthetic notifications. This does not mount, unmount or rename any storage.
void lf_test_post_volume_notification(int kind) {
    @autoreleasepool {
        NSArray *names = @[NSWorkspaceDidMountNotification, NSWorkspaceDidUnmountNotification, NSWorkspaceDidRenameVolumeNotification];
        if (kind < 0 || kind > 2) return;
        [NSWorkspace.sharedWorkspace.notificationCenter postNotificationName:names[kind] object:NSWorkspace.sharedWorkspace];
    }
}

#import <objc/runtime.h>
#include <sys/resource.h>
// Scoped metadata simulation for task fixtures only; no iCloud account/settings or
// on-disk cloud flags are changed. All unregistered URLs retain Foundation behavior.
static NSMutableDictionary<NSString *, NSNumber *> *cloudFixtures;
static int unguardedMediaChecks;
@interface NSURL (LFCloudFixture)
- (BOOL)lf_fixtureValue:(id *)value forKey:(NSURLResourceKey)key error:(NSError **)error;
@end
@implementation NSURL (LFCloudFixture)
- (BOOL)lf_fixtureValue:(id *)value forKey:(NSURLResourceKey)key error:(NSError **)error {
    NSNumber *mode;
    @synchronized (NSURL.class) { mode = cloudFixtures[self.path]; }
    if (mode && [key isEqualToString:NSURLIsUbiquitousItemKey]) { *value = @YES; return YES; }
    if (mode && [key isEqualToString:NSURLIsAliasFileKey] && mode.intValue == 3) { *value = @YES; return YES; }
    if (mode && [key isEqualToString:NSURLUbiquitousItemDownloadingStatusKey]) {
        @synchronized (NSURL.class) {
            if (getiopolicy_np(IOPOL_TYPE_VFS_MATERIALIZE_DATALESS_FILES,IOPOL_SCOPE_THREAD) != IOPOL_MATERIALIZE_DATALESS_FILES_OFF) unguardedMediaChecks++;
        }
        if (mode.intValue == 4) return NO;
        *value = mode.intValue == 2 ? NSURLUbiquitousItemDownloadingStatusNotDownloaded : NSURLUbiquitousItemDownloadingStatusCurrent;
        return YES;
    }
    return [self lf_fixtureValue:value forKey:key error:error];
}
@end
void lf_test_cloud_fixture(const char *path, int mode) {
    static dispatch_once_t once;
    dispatch_once(&once, ^{
        cloudFixtures = [NSMutableDictionary new];
        method_exchangeImplementations(class_getInstanceMethod(NSURL.class,@selector(getResourceValue:forKey:error:)),
            class_getInstanceMethod(NSURL.class,@selector(lf_fixtureValue:forKey:error:)));
    });
    @synchronized (NSURL.class) {
        if (mode == 0) [cloudFixtures removeObjectForKey:@(path)];
        else cloudFixtures[@(path)] = @(mode);
    }
}
int lf_test_unguarded_cloud_checks(void) { @synchronized(NSURL.class) { return unguardedMediaChecks; } }
int lf_test_materialization_policy(void) { return getiopolicy_np(IOPOL_TYPE_VFS_MATERIALIZE_DATALESS_FILES,IOPOL_SCOPE_THREAD); }
