#import <Foundation/Foundation.h>
#include <sys/mount.h>
#include <sys/attr.h>
int main(int argc, char **argv) {
  @autoreleasepool {
    if (argc != 2) return 2;
    struct statfs fs;
    if(statfs(argv[1], &fs)) return 3;
    struct attrlist attrs={0}; attrs.bitmapcount=ATTR_BIT_MAP_COUNT;
    attrs.volattr=ATTR_VOL_INFO|ATTR_VOL_CAPABILITIES;
    struct {uint32_t length;vol_capabilities_attr_t caps;} data={0};
    BOOL valid=getattrlist(argv[1],&attrs,&data,sizeof(data),0)==0;
    NSMutableArray *caps=[NSMutableArray new], *known=[NSMutableArray new];
    for(int i=0;i<4;i++){[caps addObject:@(data.caps.capabilities[i])];[known addObject:@(data.caps.valid[i])];}
    NSURL *url=[NSURL fileURLWithPath:[NSString stringWithUTF8String:argv[1]]];
    NSMutableDictionary *result=[@{@"flags":@(fs.f_flags),@"readOnly":@((BOOL)((fs.f_flags&MNT_RDONLY)!=0)),
      @"local":@((BOOL)((fs.f_flags&MNT_LOCAL)!=0)),@"filesystem":[NSString stringWithUTF8String:fs.f_fstypename],
      @"capabilitiesAvailable":@(valid),@"capabilityBits":caps,@"validCapabilityBits":known} mutableCopy];
    for(NSString *key in @[NSURLVolumeIsEncryptedKey,NSURLVolumeIsInternalKey,NSURLVolumeIsLocalKey,NSURLVolumeUUIDStringKey]){
      id value=nil; if([url getResourceValue:&value forKey:key error:NULL] && value)result[key]=value;
      else result[key]=@"Unknown";
    }
    NSData *json=[NSJSONSerialization dataWithJSONObject:result options:NSJSONWritingPrettyPrinted error:NULL];
    fwrite(json.bytes,1,json.length,stdout);puts("");
  }return 0;
}
