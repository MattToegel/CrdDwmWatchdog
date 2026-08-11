namespace CrdDwmWatchdog;

internal static class HealthEvaluator
{
    public static HealthAssessment Evaluate(
        HealthSample sample,
        WatchdogSettings settings,
        BaselineState? baseline)
    {
        var reasons = new List<string>();
        var dwmProblem = false;
        var crdProblem = false;

        if (!string.IsNullOrWhiteSpace(sample.Error))
        {
            reasons.Add($"Sampling error: {sample.Error}");
        }

        if (sample.Dwm is { } dwm)
        {
            if (dwm.PrivateMb >= settings.DwmPrivateMbThreshold)
            {
                dwmProblem = true;
                reasons.Add($"DWM private memory is {dwm.PrivateMb:N0} MB " +
                            $"(threshold {settings.DwmPrivateMbThreshold:N0} MB).");
            }

            if (baseline?.DwmPrivateMb is { } cleanDwm &&
                dwm.PrivateMb - cleanDwm >= settings.DwmGrowthOverBaselineMb)
            {
                dwmProblem = true;
                reasons.Add($"DWM grew {dwm.PrivateMb - cleanDwm:N0} MB above its clean baseline.");
            }
        }
        else
        {
            reasons.Add("DWM could not be inspected.");
        }

        if (!sample.CrdActivity.IsLikelyActive && sample.MainCrdProcess is { } crd)
        {
            if (crd.PrivateMb >= settings.IdleCrdPrivateMbThreshold)
            {
                crdProblem = true;
                reasons.Add($"Idle CRD private memory is {crd.PrivateMb:N0} MB " +
                            $"(threshold {settings.IdleCrdPrivateMbThreshold:N0} MB).");
            }

            if (crd.CpuCorePercent >= settings.IdleCrdCpuCorePercentThreshold)
            {
                crdProblem = true;
                reasons.Add($"Idle CRD used {crd.CpuCorePercent:N1}% of one CPU core.");
            }

            if (crd.Handles >= settings.IdleCrdHandleThreshold)
            {
                crdProblem = true;
                reasons.Add($"Idle CRD has {crd.Handles:N0} handles.");
            }
        }

        return new HealthAssessment(dwmProblem, crdProblem, reasons);
    }
}
