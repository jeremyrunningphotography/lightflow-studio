#import <Foundation/Foundation.h>
#include <sys/mount.h>
#include <sys/stat.h>
#include <sys/attr.h>
#include <unistd.h>
#include <errno.h>
#include <stdlib.h>
#include <string.h>
#include <fcntl.h>
#include <stdatomic.h>
#include <dirent.h>
#include <sys/resource.h>
#include <CommonCrypto/CommonDigest.h>

static atomic_int activeDescriptors = 0;

@interface LFMountBinding : NSObject
@property(nonatomic) int descriptor;
@property(nonatomic, strong) NSString *epoch;
@end
@implementation LFMountBinding
- (instancetype)init { self = [super init]; if (self) _descriptor = -1; return self; }
- (void)dealloc { if (_descriptor >= 0) { close(_descriptor); atomic_fetch_sub(&activeDescriptors, 1); } }
@end

@interface LFStorageContext : NSObject
@property(nonatomic, strong) NSMutableDictionary<NSString *, LFMountBinding *> *mounts;
@end
@implementation LFStorageContext
- (instancetype)init { self = [super init]; if (self) _mounts = [NSMutableDictionary new]; return self; }
@end

// Public Darwin/Foundation APIs only. No catalog open, write probe, or storage creation.
static NSString *objectIdentity(struct stat s) {
    return [NSString stringWithFormat:@"%u:%llu:%u:%lld:%ld", (unsigned)s.st_dev,
        (unsigned long long)s.st_ino, s.st_gen, (long long)s.st_birthtimespec.tv_sec,
        s.st_birthtimespec.tv_nsec];
}

static NSDictionary *probe(LFStorageContext *context, NSString *input, BOOL permitMissingLeaf, BOOL mediaRead) {
    if (![input isAbsolutePath])
        return @{@"error": @"An absolute NUL-free filesystem path is required."};
    const char *path = input.fileSystemRepresentation;
    struct stat entry;
    BOOL exists = lstat(path, &entry) == 0;
    int entryError = exists ? 0 : errno;
    NSString *observed = input;
    NSString *leaf = @"";
    if (!exists) {
        if (entryError != ENOENT || !permitMissingLeaf)
            return @{@"error": @"Location unavailable or inaccessible; preserve configured storage.", @"errno": @(entryError)};
        leaf = input.lastPathComponent;
        if (!leaf.length || [leaf isEqualToString:@"."] || [leaf isEqualToString:@".."])
            return @{@"error": @"Invalid intended creation leaf."};
        observed = input.stringByDeletingLastPathComponent;
        if (lstat(observed.fileSystemRepresentation, &entry) != 0)
            return @{@"error": @"The immediate creation parent is unavailable; no recursive fallback.", @"errno": @(errno)};
    }
    char resolved[PATH_MAX];
    if (!realpath(observed.fileSystemRepresentation, resolved))
        return @{@"error": @"Path resolution failed (including broken links).", @"errno": @(errno)};
    NSString *canonicalParent = [[NSFileManager defaultManager] stringWithFileSystemRepresentation:resolved length:strlen(resolved)];
    NSString *canonical = exists ? canonicalParent : [canonicalParent stringByAppendingPathComponent:leaf];
    struct stat target, parent;
    if (stat(resolved, &target) != 0)
        return @{@"error": @"Resolved target could not be inspected.", @"errno": @(errno)};
    if (!exists && !S_ISDIR(target.st_mode))
        return @{@"error": @"Intended creation parent is not a directory."};
    NSString *parentPath = exists && !S_ISDIR(target.st_mode) ? canonicalParent.stringByDeletingLastPathComponent : canonicalParent;
    if (stat(parentPath.fileSystemRepresentation, &parent) != 0)
        return @{@"error": @"Companion-file parent unavailable.", @"errno": @(errno)};
    struct statfs fs;
    if (statfs(resolved, &fs) != 0)
        return @{@"error": @"Resolved filesystem could not be inspected.", @"errno": @(errno)};
    // A link's destination can be inspected, but this slice does not approve linked containment.
    BOOL ambiguous = NO;
    NSString *prefix = @"/";
    for (NSString *part in observed.pathComponents) {
        if ([part isEqualToString:@"/"]) continue;
        DIR *directory = opendir(prefix.fileSystemRepresentation);
        BOOL exactSpelling = NO;
        if (directory) {
            struct dirent *item;
            while ((item = readdir(directory))) {
                if (strcmp(item->d_name, part.fileSystemRepresentation) == 0) { exactSpelling = YES; break; }
            }
            closedir(directory);
        }
        // OS lookup equivalence is not permission to collapse case or Unicode spellings.
        if (!exactSpelling) ambiguous = YES;
        prefix = [prefix stringByAppendingPathComponent:part];
        struct stat component;
        if (lstat(prefix.fileSystemRepresentation, &component) != 0 || S_ISLNK(component.st_mode)) ambiguous = YES;
        NSNumber *alias = nil, *ubiquitous = nil;
        NSURL *url = [NSURL fileURLWithPath:prefix];
        if (![url getResourceValue:&alias forKey:NSURLIsAliasFileKey error:NULL] || alias.boolValue) ambiguous = YES;
        if (![url getResourceValue:&ubiquitous forKey:NSURLIsUbiquitousItemKey error:NULL]) ambiguous = YES;
        // Media reads may traverse a cloud-managed directory with resolved identity.
        // Catalog/cache roles retain the original conservative cloud restriction.
        if (ubiquitous.boolValue && !mediaRead) ambiguous = YES;
        if (mediaRead && (component.st_flags & SF_DATALESS) != 0)
            return @{@"cloudUnavailable":@YES, @"error":@"Cloud-only media is not resident. Download it explicitly in Finder, then retry; Lightflow will not download it."};
        if (mediaRead && S_ISREG(component.st_mode)) {
            NSString *status = nil;
            if (ubiquitous.boolValue &&
                (![url getResourceValue:&status forKey:NSURLUbiquitousItemDownloadingStatusKey error:NULL] ||
                 (![status isEqualToString:NSURLUbiquitousItemDownloadingStatusCurrent] &&
                  ![status isEqualToString:NSURLUbiquitousItemDownloadingStatusDownloaded])))
                return @{@"cloudUnavailable":@YES, @"error":@"Cloud media residency is unavailable. Make it available locally in Finder, then retry; Lightflow will not download it."};
        }
    }
    struct attrlist attrs = {0};
    attrs.bitmapcount = ATTR_BIT_MAP_COUNT;
    attrs.volattr = ATTR_VOL_INFO | ATTR_VOL_CAPABILITIES;
    struct { uint32_t length; vol_capabilities_attr_t caps; } caps = {0};
    BOOL hasCaps = getattrlist(resolved, &attrs, &caps, sizeof(caps), 0) == 0;
    NSNumber *urlLocal = nil;
    BOOL hasLocal = [[NSURL fileURLWithPath:canonicalParent] getResourceValue:&urlLocal forKey:NSURLVolumeIsLocalKey error:NULL];
    NSNumber *internal = nil;
    BOOL internalKnown = [[NSURL fileURLWithPath:canonicalParent] getResourceValue:&internal forKey:NSURLVolumeIsInternalKey error:NULL] && internal != nil;
    NSString *locality = !hasLocal || urlLocal.boolValue != ((fs.f_flags & MNT_LOCAL) != 0) ? @"Unknown" : urlLocal.boolValue ? @"Local" : @"Network";
    NSString *type = [NSString stringWithUTF8String:fs.f_fstypename];
    NSString *mount = [NSString stringWithUTF8String:fs.f_mntonname];
    NSString *source = [NSString stringWithUTF8String:fs.f_mntfromname];
    NSMutableArray *ancestry = [NSMutableArray new];
    NSString *ancestor = canonicalParent;
    while (YES) {
        struct stat ancestorStat;
        if (stat(ancestor.fileSystemRepresentation, &ancestorStat) != 0)
            return @{@"error":@"Resolved ancestry could not establish containment.", @"errno":@(errno)};
        [ancestry addObject:objectIdentity(ancestorStat)];
        if ([ancestor isEqualToString:@"/"]) break;
        ancestor = ancestor.stringByDeletingLastPathComponent;
    }
    NSString *mountKey = [NSString stringWithFormat:@"%d:%d:%@", fs.f_fsid.val[0], fs.f_fsid.val[1], mount];
    LFMountBinding *binding = context.mounts[mountKey];
    struct statfs anchorFS;
    struct stat anchor, mountRoot;
    BOOL sameAnchor = binding && fstatfs(binding.descriptor, &anchorFS) == 0
        && fstat(binding.descriptor, &anchor) == 0 && stat(mount.fileSystemRepresentation, &mountRoot) == 0
        && memcmp(&anchorFS.f_fsid, &fs.f_fsid, sizeof(fs.f_fsid)) == 0
        && [objectIdentity(anchor) isEqualToString:objectIdentity(mountRoot)];
    if (!sameAnchor) {
        [context.mounts removeObjectForKey:mountKey];
        binding = [LFMountBinding new];
        // A private read-only mount anchor distinguishes the live mount from a reused fsid.
        // Ordinary unmount may remain busy until the owning assessor is disposed.
        binding.descriptor = open(mount.fileSystemRepresentation, O_RDONLY | O_DIRECTORY | O_CLOEXEC | O_NOFOLLOW);
        if (binding.descriptor >= 0) atomic_fetch_add(&activeDescriptors, 1);
        if (binding.descriptor < 0 || fstatfs(binding.descriptor, &anchorFS) != 0
            || memcmp(&anchorFS.f_fsid, &fs.f_fsid, sizeof(fs.f_fsid)) != 0)
            return @{@"error":@"Cannot bind the resolved mount; no admission.", @"errno":@(errno)};
        binding.epoch = [NSUUID UUID].UUIDString;
        context.mounts[mountKey] = binding;
    }
    BOOL writable = !(fs.f_flags & MNT_RDONLY) && access(parentPath.fileSystemRepresentation, W_OK | X_OK) == 0
        && (!exists || access(resolved, W_OK) == 0);
    BOOL readable = access(resolved, R_OK) == 0 && (!S_ISDIR(target.st_mode) || access(resolved, X_OK) == 0);
    BOOL lockingKnown = hasCaps && (caps.caps.valid[VOL_CAPABILITIES_INTERFACES] & VOL_CAP_INT_ADVLOCK);
    BOOL locking = lockingKnown && (caps.caps.capabilities[VOL_CAPABILITIES_INTERFACES] & VOL_CAP_INT_ADVLOCK);
    // Double observation detects ordinary substitutions during the probe; not an atomic-use lease.
    struct stat again, requestedAgain;
    struct statfs fsAgain;
    BOOL unchanged = stat(resolved, &again) == 0 && statfs(resolved, &fsAgain) == 0
        && [objectIdentity(target) isEqualToString:objectIdentity(again)]
        && memcmp(&fs.f_fsid, &fsAgain.f_fsid, sizeof(fs.f_fsid)) == 0;
    if (exists) unchanged = unchanged && lstat(path, &requestedAgain) == 0
        && [objectIdentity(entry) isEqualToString:objectIdentity(requestedAgain)];
    else unchanged = unchanged && lstat(path, &requestedAgain) != 0 && errno == ENOENT;
    if (!unchanged) return @{@"error": @"Path or mount changed during assessment; restart preflight."};
    return @{@"canonical":canonical, @"target":objectIdentity(target), @"parent":objectIdentity(parent),
        @"missingLeaf":leaf, @"ancestors":ancestry, @"locality":locality, @"filesystem":type,
        @"fsid":[NSString stringWithFormat:@"%d:%d", fs.f_fsid.val[0], fs.f_fsid.val[1]],
        @"mount":mount, @"source":source, @"mountEpoch":binding.epoch, @"ambiguous":@(ambiguous), @"read":@(readable),
        @"write":@(writable), @"internalKnown":@(internalKnown), @"internal":@(internal.boolValue), @"lockingKnown":@(lockingKnown), @"locking":@(locking),
        @"caseKnown":@((BOOL)(hasCaps && (caps.caps.valid[VOL_CAPABILITIES_FORMAT] & VOL_CAP_FMT_CASE_SENSITIVE) != 0)),
        @"caseSensitive":@((BOOL)(hasCaps && (caps.caps.capabilities[VOL_CAPABILITIES_FORMAT] & VOL_CAP_FMT_CASE_SENSITIVE) != 0))};
}

__attribute__((visibility("default"))) void *lf_storage_create(void) {
    return (__bridge_retained void *)[LFStorageContext new];
}
__attribute__((visibility("default"))) void lf_storage_destroy(void *value) {
    LFStorageContext *context = (__bridge_transfer LFStorageContext *)value;
    (void)context;
}
__attribute__((visibility("default"))) char *lf_storage_probe(void *value, const char *path, int permitMissingLeaf) {
    @autoreleasepool {
        @try {
            NSString *input = [[NSString alloc] initWithUTF8String:path];
            LFStorageContext *context = (__bridge LFStorageContext *)value;
            NSDictionary *facts = input && context ? probe(context, input, permitMissingLeaf != 0, NO) : @{@"error":@"Invalid native context or UTF-8 path."};
            NSData *data = [NSJSONSerialization dataWithJSONObject:facts options:0 error:NULL];
            if (!data) return NULL;
            char *result = malloc(data.length + 1);
            if (!result) return NULL;
            memcpy(result, data.bytes, data.length); result[data.length] = 0;
            return result;
        } @catch (NSException *exception) {
            (void)exception;
            return strdup("{\"error\":\"Native assessment failed; preserve storage.\"}");
        }
    }
}
__attribute__((visibility("default"))) void lf_storage_free(char *result) { free(result); }
__attribute__((visibility("default"))) int lf_storage_active_descriptors(void) { return atomic_load(&activeDescriptors); }

// The policy is thread-scoped, covers the entire synchronous native operation,
// and is restored before returning to the managed thread pool, including errors.
static char *mediaOperation(void *value, const char *path, NSString *other, int operation) {
    @autoreleasepool {
        int previous = getiopolicy_np(IOPOL_TYPE_VFS_MATERIALIZE_DATALESS_FILES, IOPOL_SCOPE_THREAD);
        if (previous < 0 || setiopolicy_np(IOPOL_TYPE_VFS_MATERIALIZE_DATALESS_FILES,
                IOPOL_SCOPE_THREAD, IOPOL_MATERIALIZE_DATALESS_FILES_OFF) != 0)
            return strdup("{\"error\":\"Cannot prevent cloud downloads; media read refused.\"}");
        NSDictionary *result = nil;
        int fd = -1;
        @try {
            LFStorageContext *context = (__bridge LFStorageContext *)value;
            NSString *input = path ? [[NSString alloc] initWithUTF8String:path] : nil;
            result = input && context ? probe(context, input, NO, YES) : @{@"error":@"Invalid media context or path."};
            if (!result[@"error"] && operation == 1) {
                // Only an absent top-level volume boundary is excludable. A missing
                // folder on a live volume, alias, denied path or unknown mount is not.
                NSArray *parts = other.pathComponents;
                NSString *boundary = parts.count >= 3 ? [@"/Volumes" stringByAppendingPathComponent:parts[2]] : @"";
                BOOL unrelated = parts.count >= 3 && [parts[1] isEqualToString:@"Volumes"] &&
                    ![parts containsObject:@".."] && ![parts containsObject:@"."] &&
                    ![input isEqualToString:other] &&
                    ![input hasPrefix:[other stringByAppendingString:@"/"]] &&
                    ![other hasPrefix:[input stringByAppendingString:@"/"]];
                struct stat missing;
                unrelated = unrelated && lstat(boundary.fileSystemRepresentation, &missing) != 0 && errno == ENOENT;
                NSDictionary *absent = unrelated ? probe(context, boundary, YES, YES) : nil;
                NSString *mount = result[@"mount"];
                unrelated = unrelated && absent && !absent[@"error"] && ![absent[@"ambiguous"] boolValue] &&
                    ![result[@"ambiguous"] boolValue] && [absent[@"missingLeaf"] isEqualToString:parts[2]] &&
                    [absent[@"canonical"] isEqualToString:boundary] && [mount hasPrefix:@"/Volumes/"] &&
                    ![mount isEqualToString:boundary] && ![result[@"fsid"] isEqualToString:absent[@"fsid"]];
                int capacity = getfsstat(NULL, 0, MNT_NOWAIT);
                struct statfs *mounts = capacity > 0 ? calloc((size_t)capacity, sizeof(struct statfs)) : NULL;
                int count = mounts ? getfsstat(mounts, (int)(capacity * sizeof(struct statfs)), MNT_NOWAIT) : -1;
                if (count > capacity) { count = -1; unrelated = NO; }
                BOOL candidateMounted = NO;
                if (count <= 0) unrelated = NO;
                for (int i = 0; i < count; i++) {
                    NSString *at = @(mounts[i].f_mntonname);
                    if ([at isEqualToString:boundary]) unrelated = NO;
                    if ([at isEqualToString:mount]) candidateMounted = YES;
                }
                free(mounts);
                NSDictionary *again = unrelated ? probe(context, input, NO, YES) : nil;
                unrelated = unrelated && candidateMounted && again && !again[@"error"] &&
                    [again[@"target"] isEqual:result[@"target"]] && [again[@"mountEpoch"] isEqual:result[@"mountEpoch"]] &&
                    lstat(boundary.fileSystemRepresentation, &missing) != 0 && errno == ENOENT;
                result = @{@"unrelated":@(unrelated)};
            } else if (!result[@"error"] && operation == 2) {
                if ([result[@"ambiguous"] boolValue]) result = @{@"error":@"Unsafe media identity (alias, link or unresolved spelling). Review the source path, then retry."};
                else {
                    fd = open(path, O_RDONLY | O_CLOEXEC | O_NOFOLLOW_ANY | O_NONBLOCK);
                    struct stat before, after, entry;
                    if (fd < 0 || fstat(fd, &before) != 0 || !S_ISREG(before.st_mode) ||
                        (before.st_flags & SF_DATALESS) != 0 || ![objectIdentity(before) isEqual:result[@"target"]])
                        result = @{@"error":@"Media is not safely available locally. Make it available in Finder and retry; no cloud download was requested."};
                    else {
                        const size_t sample = 64 * 1024;
                        unsigned char bytes[128 * 1024 + 64];
                        const char domain[] = "Lightflow.SourceFingerprint.v1";
                        size_t used = sizeof(domain); memcpy(bytes, domain, used);
                        uint64_t length = (uint64_t)before.st_size;
                        for (int i = 0; i < 8; i++) bytes[used++] = (unsigned char)(length >> (8*i));
                        BOOL ok = YES;
                        for (int part = 0; part < (length > sample ? 2 : 1); part++) {
                            off_t offset = part == 0 ? 0 : (off_t)MAX(sample, length - sample);
                            size_t wanted = (size_t)MIN(sample, length - (uint64_t)offset), got = 0;
                            while (got < wanted) {
                                ssize_t n = pread(fd, bytes + used + got, wanted - got, offset + (off_t)got);
                                if (n <= 0) { ok = NO; break; } got += (size_t)n;
                            }
                            used += got;
                        }
                        ok = ok && fstat(fd, &after) == 0 && lstat(path, &entry) == 0 &&
                            [objectIdentity(before) isEqual:objectIdentity(after)] && [objectIdentity(before) isEqual:objectIdentity(entry)] &&
                            before.st_size == after.st_size && before.st_mtimespec.tv_sec == after.st_mtimespec.tv_sec &&
                            before.st_mtimespec.tv_nsec == after.st_mtimespec.tv_nsec;
                        if (!ok) result = @{@"error":@"Source changed or could not be read locally. Retry; Catalog records are preserved."};
                        else {
                            unsigned char digest[CC_SHA256_DIGEST_LENGTH]; CC_SHA256(bytes, (CC_LONG)used, digest);
                            NSMutableString *hash = [NSMutableString new];
                            for (int i = 0; i < CC_SHA256_DIGEST_LENGTH; i++) [hash appendFormat:@"%02x", digest[i]];
                            result = @{@"fingerprint":hash};
                        }
                    }
                }
            }
        } @catch (NSException *exception) {
            (void)exception; result = @{@"error":@"Native media identity unavailable; read refused."};
        } @finally {
            if (fd >= 0) close(fd);
            if (setiopolicy_np(IOPOL_TYPE_VFS_MATERIALIZE_DATALESS_FILES, IOPOL_SCOPE_THREAD, previous) != 0)
                result = @{@"error":@"Could not restore thread media policy; read refused."};
        }
        NSData *data = [NSJSONSerialization dataWithJSONObject:result options:0 error:NULL];
        if (!data) return NULL;
        char *output = malloc(data.length + 1);
        if (!output) return NULL;
        memcpy(output, data.bytes, data.length); output[data.length] = 0; return output;
    }
}
__attribute__((visibility("default"))) char *lf_media_probe(void *context, const char *path) { return mediaOperation(context,path,nil,0); }
__attribute__((visibility("default"))) char *lf_media_unrelated_offline(void *context, const char *path, const char *other) { return mediaOperation(context,path,@(other),1); }
__attribute__((visibility("default"))) char *lf_media_fingerprint(void *context, const char *path) { return mediaOperation(context,path,nil,2); }
