/* Read-only macOS research adapter; not production storage policy. */
#include <sys/mount.h>
#include <stdio.h>
int main(int argc,char **argv){
 if(argc!=2)return 2;
 struct statfs s;
 if(statfs(argv[1],&s)!=0){perror("statfs");return 1;}
 printf("{\"filesystem\":\"%s\",\"local\":%s,\"readOnly\":%s}\n",s.f_fstypename,(s.f_flags&MNT_LOCAL)?"true":"false",(s.f_flags&MNT_RDONLY)?"true":"false");
 return 0;
}
