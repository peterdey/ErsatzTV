namespace ErsatzTV.FFmpeg.Decoder;

public class DecoderRkmpp : DecoderBase
{
    protected override FrameDataLocation OutputFrameDataLocation => FrameDataLocation.Software;
    public override string Name => "implicit_rkmpp";
    public override string[] InputOptions(InputFile inputFile) => Array.Empty<string>();
}
