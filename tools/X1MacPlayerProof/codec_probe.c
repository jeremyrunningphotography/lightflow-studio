#include <VideoToolbox/VideoToolbox.h>
#include <stdio.h>
int main(void){
    printf("{\"h264_hardware_supported\":%d,\"hevc_hardware_supported\":%d,\"av1_hardware_supported\":%d,\"prores422_hardware_supported\":%d}\n",VTIsHardwareDecodeSupported(kCMVideoCodecType_H264),VTIsHardwareDecodeSupported(kCMVideoCodecType_HEVC),VTIsHardwareDecodeSupported(kCMVideoCodecType_AV1),VTIsHardwareDecodeSupported(kCMVideoCodecType_AppleProRes422));
    return 0;
}
