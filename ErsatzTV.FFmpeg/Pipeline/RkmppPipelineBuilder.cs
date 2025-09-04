using ErsatzTV.FFmpeg.Capabilities;
using ErsatzTV.FFmpeg.Decoder;
using ErsatzTV.FFmpeg.Encoder;
using ErsatzTV.FFmpeg.Encoder.Rkmpp;
using ErsatzTV.FFmpeg.Filter;
using ErsatzTV.FFmpeg.Filter.Rkmpp;
using ErsatzTV.FFmpeg.Format;
using ErsatzTV.FFmpeg.GlobalOption.HardwareAcceleration;
using ErsatzTV.FFmpeg.OutputFormat;
using ErsatzTV.FFmpeg.OutputOption;
using Microsoft.Extensions.Logging;

namespace ErsatzTV.FFmpeg.Pipeline;

public class RkmppPipelineBuilder : SoftwarePipelineBuilder
{
    private readonly IHardwareCapabilities _hardwareCapabilities;
    private readonly ILogger _logger;

    public RkmppPipelineBuilder(
        IFFmpegCapabilities ffmpegCapabilities,
        IHardwareCapabilities hardwareCapabilities,
        HardwareAccelerationMode hardwareAccelerationMode,
        Option<VideoInputFile> videoInputFile,
        Option<AudioInputFile> audioInputFile,
        Option<WatermarkInputFile> watermarkInputFile,
        Option<SubtitleInputFile> subtitleInputFile,
        Option<ConcatInputFile> concatInputFile,
        Option<GraphicsEngineInput> graphicsEngineInput,
        string reportsFolder,
        string fontsFolder,
        ILogger logger) : base(
        ffmpegCapabilities,
        hardwareAccelerationMode,
        videoInputFile,
        audioInputFile,
        watermarkInputFile,
        subtitleInputFile,
        concatInputFile,
        graphicsEngineInput,
        reportsFolder,
        fontsFolder,
        logger)
    {
        _hardwareCapabilities = hardwareCapabilities;
        _logger = logger;
        _logger.LogDebug("Using RkmppPipelineBuilder");
    }

    protected override FFmpegState SetAccelState(
        VideoStream videoStream,
        FFmpegState ffmpegState,
        FrameState desiredState,
        PipelineContext context,
        ICollection<IPipelineStep> pipelineSteps)
    {
        FFmpegCapability decodeCapability = _hardwareCapabilities.CanDecode(
            videoStream.Codec,
            videoStream.Profile,
            videoStream.PixelFormat,
            videoStream.ColorParams.IsHdr);
        FFmpegCapability encodeCapability = _hardwareCapabilities.CanEncode(
            desiredState.VideoFormat,
            desiredState.VideoProfile,
            desiredState.PixelFormat);

        // use software encoding (rawvideo) when piping to parent hls segmenter
        if (ffmpegState.OutputFormat is OutputFormatKind.Nut)
        {
            encodeCapability = FFmpegCapability.Software;
            _logger.LogDebug("Using software encoder");
        }

        if (decodeCapability is FFmpegCapability.Hardware)
        {
            pipelineSteps.Add(new RkmppHardwareAccelerationOption());
            _logger.LogDebug("Using RkmppHardwareAccelerationOption decoder");
        }

        // disable hw accel if decoder/encoder isn't supported
        return ffmpegState with
        {
            DecoderHardwareAccelerationMode = decodeCapability == FFmpegCapability.Hardware
                ? HardwareAccelerationMode.Rkmpp
                : HardwareAccelerationMode.None,
            EncoderHardwareAccelerationMode = encodeCapability == FFmpegCapability.Hardware
                ? HardwareAccelerationMode.Rkmpp
                : HardwareAccelerationMode.None
        };
    }

    protected override Option<IDecoder> SetDecoder(
        VideoInputFile videoInputFile,
        VideoStream videoStream,
        FFmpegState ffmpegState,
        PipelineContext context)
    {
        Option<IDecoder> maybeDecoder = (ffmpegState.DecoderHardwareAccelerationMode, videoStream.Codec) switch
        {
            (HardwareAccelerationMode.Rkmpp, _) => new DecoderRkmpp(),

            _ => GetSoftwareDecoder(videoStream)
        };

        foreach (IDecoder decoder in maybeDecoder)
        {
            videoInputFile.AddOption(decoder);
            return Some(decoder);
        }

        return None;
    }

    protected override Option<IEncoder> GetEncoder(
        FFmpegState ffmpegState,
        FrameState currentState,
        FrameState desiredState) =>
        (ffmpegState.EncoderHardwareAccelerationMode, desiredState.VideoFormat) switch
        {
            (HardwareAccelerationMode.Rkmpp, VideoFormat.Hevc) =>
                new EncoderHevcRkmpp(desiredState.BitDepth),
            (HardwareAccelerationMode.Rkmpp, VideoFormat.H264) =>
                new EncoderH264Rkmpp(desiredState.VideoProfile),

            _ => GetSoftwareEncoder(ffmpegState, currentState, desiredState)
        };

    protected override List<IPipelineFilterStep> SetPixelFormat(
        VideoStream videoStream,
        Option<IPixelFormat> desiredPixelFormat,
        FrameState currentState,
        ICollection<IPipelineStep> pipelineSteps)
    {
        var result = new List<IPipelineFilterStep>();

        foreach (IPixelFormat pixelFormat in desiredPixelFormat)
        {
            IPixelFormat format = pixelFormat;

            if (pixelFormat is PixelFormatNv12 nv12)
            {
                foreach (IPixelFormat pf in AvailablePixelFormats.ForPixelFormat(nv12.Name, null))
                {
                    format = pf;
                }
            }

            if (!videoStream.ColorParams.IsBt709)
            {
                _logger.LogDebug("Adding colorspace filter");
                var colorspace = new ColorspaceFilter(
                    currentState,
                    videoStream,
                    format);
                currentState = colorspace.NextState(currentState);
                result.Add(colorspace);
            }

            if (currentState.PixelFormat.Map(f => f.FFmpegName) != format.FFmpegName)
            {
                _logger.LogDebug(
                    "Format {A} doesn't equal {B}",
                    currentState.PixelFormat.Map(f => f.FFmpegName),
                    format.FFmpegName);

                // Try to force NV12 format
                if (format is PixelFormatYuv420P)
                {
                    _logger.LogDebug("Pixel Format is yuv420p; changing to nv12");
                    format = new PixelFormatNv12(format.Name);
                }

                _logger.LogDebug("Adding PixelFormatOutputOption: {PixelFormat}", format);
                pipelineSteps.Add(new PixelFormatOutputOption(format));
            }
        }

        return result;
    }

    protected override FrameState SetScale(
        VideoInputFile videoInputFile,
        VideoStream videoStream,
        PipelineContext context,
        FFmpegState ffmpegState,
        FrameState desiredState,
        FrameState currentState)
    {
        IPipelineFilterStep scaleStep;
        var temp = false;

        /* if (currentState.ScaledSize != desiredState.ScaledSize && ffmpegState is
            {
                DecoderHardwareAccelerationMode: HardwareAccelerationMode.None,
                EncoderHardwareAccelerationMode: HardwareAccelerationMode.None
            } && context is { HasWatermark: false, HasSubtitleOverlay: false, ShouldDeinterlace: false } ||
            ffmpegState.DecoderHardwareAccelerationMode != HardwareAccelerationMode.Rkmpp) */
        if (temp)
        {
            scaleStep = new ScaleFilter(
                currentState,
                desiredState.ScaledSize,
                desiredState.PaddedSize,
                desiredState.CroppedSize,
                VideoStream.IsAnamorphicEdgeCase);
        }
        else
        {
            scaleStep = new ScaleRkmppFilter(
                currentState with
                {
                    PixelFormat = //context.HasWatermark ||
                    //context.HasSubtitleOverlay ||
                    // (desiredState.ScaledSize != desiredState.PaddedSize) ||
                    // context.HasSubtitleText ||
                    ffmpegState is
                    {
                        DecoderHardwareAccelerationMode: HardwareAccelerationMode.Rkmpp,
                        EncoderHardwareAccelerationMode: HardwareAccelerationMode.None
                    }
                        ? desiredState.PixelFormat.Map(pf => (IPixelFormat)new PixelFormatNv12(pf.Name))
                        : Option<IPixelFormat>.None
                },
                desiredState.ScaledSize,
                desiredState.PaddedSize,
                desiredState.CroppedSize,
                VideoStream.IsAnamorphicEdgeCase);
        }

        if (!string.IsNullOrWhiteSpace(scaleStep.Filter))
        {
            currentState = scaleStep.NextState(currentState);
            videoInputFile.FilterSteps.Add(scaleStep);
        }

        return currentState;
    }
}
