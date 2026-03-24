namespace MovieMaker.Services;

public sealed record EncodingOptions(
    int Width,
    int Height,
    int FrameRate,
    string AudioBitrate,
    string AudioSampleRate,
    string LibX264Preset,
    int LibX264Crf,
    string NvencPreset,
    int NvencCq,
    string QsvPreset,
    int QsvGlobalQuality,
    string AmfQuality,
    int AmfQp);
