#import <Foundation/Foundation.h>
#include <sys/mount.h>
#include <sys/stat.h>
int main(int argc,char **argv){@autoreleasepool{
  if(argc!=2)return 2;
  struct stat st;struct statfs fs;
  if(lstat(argv[1],&st)||S_ISLNK(st.st_mode)||statfs(argv[1],&fs))return 3;
  NSURL *url=[NSURL fileURLWithPath:[NSString stringWithUTF8String:argv[1]]];
  NSNumber *alias=nil,*cloud=nil;NSString *uuid=nil;
  if(![url getResourceValue:&alias forKey:NSURLIsAliasFileKey error:NULL]||alias==nil)return 4;
  BOOL cloudKnown=[url getResourceValue:&cloud forKey:NSURLIsUbiquitousItemKey error:NULL] && cloud!=nil;
  if(![url getResourceValue:&uuid forKey:NSURLVolumeUUIDStringKey error:NULL]||uuid==nil)return 6;
  NSDictionary *r=@{@"alias":@((BOOL)alias.boolValue),@"cloud":@((BOOL)cloud.boolValue),@"cloudKnown":@(cloudKnown),@"volumeUUID":uuid,
    @"filesystem":[NSString stringWithUTF8String:fs.f_fstypename],@"source":[NSString stringWithUTF8String:fs.f_mntfromname],
    @"mount":[NSString stringWithUTF8String:fs.f_mntonname],@"fsid":[NSString stringWithFormat:@"%d:%d",fs.f_fsid.val[0],fs.f_fsid.val[1]],
    @"readonly":@((BOOL)((fs.f_flags&MNT_RDONLY)!=0)),@"local":@((BOOL)((fs.f_flags&MNT_LOCAL)!=0))};
  NSData *d=[NSJSONSerialization dataWithJSONObject:r options:0 error:NULL];fwrite(d.bytes,1,d.length,stdout);puts("");
}return 0;}
