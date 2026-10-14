#include <sys/stat.h>
#include <sys/file.h>
#include <fcntl.h>
#include <unistd.h>
#include <errno.h>
#include <stdio.h>
#include <string.h>
#include <stdlib.h>
#include <limits.h>

// Darwin-only effects. No SQLite or lifecycle decisions.
static int same(struct stat a, struct stat b) {
    return a.st_dev == b.st_dev && a.st_ino == b.st_ino && a.st_gen == b.st_gen;
}
int lf_cat_unlinked(const char *path) {
    char copy[PATH_MAX];
    if (!path || path[0] != '/' || strlen(path) >= sizeof(copy)) return EINVAL;
    strcpy(copy, path);
    for (char *p = copy + 1; ; p++) {
        if (*p != '/' && *p != 0) continue;
        char saved = *p; *p = 0;
        struct stat s;
        if (lstat(copy, &s) != 0) { int e=errno; *p=saved; return e==ENOENT ? 0 : e; }
        if (S_ISLNK(s.st_mode) || (!S_ISDIR(s.st_mode) && (!S_ISREG(s.st_mode) || s.st_nlink != 1))) return EINVAL;
        *p=saved; if (!saved) return 0;
    }
}
int lf_cat_open(const char *path, int create, int writable, int lockMode, int directory) {
    int e=lf_cat_unlinked(path); if(e) {errno=e; return -1;}
    int flags=(writable?O_RDWR:O_RDONLY)|O_NOFOLLOW|O_CLOEXEC|O_NONBLOCK;
    if(create) flags|=O_CREAT;
    if(create==2) flags|=O_EXCL;
    if(directory) flags|=O_DIRECTORY;
    // Lock is acquired atomically with open, before any journal write.
    if(lockMode==1) flags|=O_SHLOCK;
    if(lockMode==2) flags|=O_EXLOCK;
    int fd=open(path,flags,0600); if(fd<0) return -1;
    struct stat a,b;
    if(fstat(fd,&a)||lstat(path,&b)||!same(a,b)||
        (directory?!S_ISDIR(a.st_mode):(!S_ISREG(a.st_mode)||a.st_nlink!=1))) {
        e=errno?errno:EINVAL;close(fd);errno=e;return -1;
    }
    return fd;
}
int lf_cat_bound(int fd,const char *path,int directory) {
    struct stat a,b;
    int e=lf_cat_unlinked(path);if(e)return e;
    if(fstat(fd,&a)||lstat(path,&b))return errno;
    if(!same(a,b)||(directory?!S_ISDIR(a.st_mode):(!S_ISREG(a.st_mode)||a.st_nlink!=1)))return ESTALE;
    return 0;
}
int lf_cat_regular(int fd) { struct stat s; if(fstat(fd,&s))return errno;return S_ISREG(s.st_mode)&&s.st_nlink==1?0:EINVAL; }
int lf_cat_identity(int fd,char *buffer,int size) {
    struct stat s;if(fstat(fd,&s))return errno;
    int n=snprintf(buffer,size,"%d:%llu:%u",s.st_dev,(unsigned long long)s.st_ino,s.st_gen);
    return n<0||n>=size?ERANGE:0;
}
int lf_cat_flush(int fd,int parent) {
    if(fsync(fd))return errno;
    if(fcntl(fd,F_FULLFSYNC))return errno;
    if(parent>=0 && fsync(parent))return errno;
    return 0;
}
int lf_cat_sync_directory(int fd) { return fsync(fd)==0?0:errno; }
int lf_cat_duplicate(int fd) { return fcntl(fd,F_DUPFD_CLOEXEC,0); }
