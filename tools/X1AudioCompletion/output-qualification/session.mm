#import <Foundation/Foundation.h>
#import <ApplicationServices/ApplicationServices.h>
int main(){@autoreleasepool{CFDictionaryRef x=CGSessionCopyCurrentDictionary();NSDictionary*d=(__bridge NSDictionary*)x;NSDictionary*r=@{@"screenLockedReported":d[@"CGSSessionScreenIsLocked"]?:@"not reported",@"onConsole":d[@"kCGSSessionOnConsoleKey"]?:@"not reported",@"loginDone":d[@"kCGSessionLoginDoneKey"]?:@"not reported"};NSData*j=[NSJSONSerialization dataWithJSONObject:r options:NSJSONWritingPrettyPrinted error:NULL];fwrite(j.bytes,1,j.length,stdout);puts("");if(x)CFRelease(x);}}
