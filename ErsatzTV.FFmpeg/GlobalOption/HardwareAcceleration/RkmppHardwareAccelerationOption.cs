using ErsatzTV.FFmpeg.Capabilities;
using ErsatzTV.FFmpeg.Format;

﻿namespace ErsatzTV.FFmpeg.GlobalOption.HardwareAcceleration;

public class RkmppHardwareAccelerationOption : GlobalOption
{
    // TODO: read this from ffmpeg output
    private readonly List<string> _supportedFFmpegFormats = new()
    {
        FFmpegFormat.NV12,
        FFmpegFormat.NV15
    };

    public override string[] GlobalOptions => new[] { "-hwaccel", "rkmpp" };

    // Try to force nv12 pixel format
    public override FrameState NextState(FrameState currentState)
    {
        FrameState result = currentState;

        foreach (IPixelFormat pixelFormat in currentState.PixelFormat)
        {
            if (_supportedFFmpegFormats.Contains(pixelFormat.FFmpegName))
            {
                return result;
            }

            return result with { PixelFormat = new PixelFormatNv12(pixelFormat.Name) };
        }

        return result with { PixelFormat = new PixelFormatNv12(new PixelFormatUnknown().Name) };
    }
}
