#import <AppKit/AppKit.h>
// Test-only synthetic notifications. This does not mount, unmount or rename any storage.
void lf_test_post_volume_notification(int kind) {
    @autoreleasepool {
        NSArray *names = @[NSWorkspaceDidMountNotification, NSWorkspaceDidUnmountNotification, NSWorkspaceDidRenameVolumeNotification];
        if (kind < 0 || kind > 2) return;
        [NSWorkspace.sharedWorkspace.notificationCenter postNotificationName:names[kind] object:NSWorkspace.sharedWorkspace];
    }
}
