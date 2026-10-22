using AssetsTools.NET;

namespace RfcEditor;

public enum SaveStage { Writing, Compressing, Verifying, Replacing, Complete }
public sealed record SaveProgress(SaveStage Stage,int Percent);

sealed class SaveProgressReporter(IProgress<SaveProgress>? target) : IAssetBundleCompressProgress
{
    SaveStage? lastStage;
    int lastPercent=-1;
    public void Report(SaveStage stage,int percent)
    {
        percent=Math.Clamp(percent,0,100);
        if(stage==lastStage && percent==lastPercent)return;
        lastStage=stage;lastPercent=percent;
        // A UI observer must never cause an otherwise valid save to fail.
        try{target?.Report(new SaveProgress(stage,percent));}catch{}
    }
    public void SetProgress(float progress)
    {
        if(!float.IsFinite(progress))return;
        int percent=Math.Clamp((int)(progress*100),0,100);
        if(lastStage==SaveStage.Compressing)percent=Math.Max(percent,lastPercent);
        Report(SaveStage.Compressing,percent);
    }
}
