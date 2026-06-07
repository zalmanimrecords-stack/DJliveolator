using Liveolator.Core.Visuals;

namespace Liveolator.Visuals.Gl;

internal sealed record ResolvedEffectParameters(
    EffectRef Reference,
    VisualEffectDescriptor Descriptor,
    IReadOnlyDictionary<string, float> Uniforms);

internal static class EffectParameterResolver
{
    public static IReadOnlyList<ResolvedEffectParameters> Resolve(
        int layer,
        IReadOnlyList<EffectRef> effects,
        IVisualEffectRegistry registry,
        IReadOnlyList<VisualMacro> macros,
        IReadOnlyDictionary<string, double> macroValues)
    {
        var resolved = new List<ResolvedEffectParameters>(effects.Count);
        foreach (EffectRef effect in effects)
        {
            if (!registry.TryGet(effect.EffectId, effect.Version, out VisualEffectDescriptor descriptor))
                continue;

            var values = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (VisualEffectParameter parameter in descriptor.Parameters)
            {
                double value = effect.Defaults.TryGetValue(parameter.Id, out double configured)
                    ? configured
                    : parameter.Default;

                foreach (VisualMacro macro in macros)
                {
                    if (macro.Target.Layer != layer
                        || !string.Equals(macro.Target.EffectInstanceId, effect.InstanceId, StringComparison.Ordinal)
                        || !string.Equals(macro.Target.Parameter, parameter.Id, StringComparison.Ordinal)
                        || !macroValues.TryGetValue(macro.Name, out double normalized))
                    {
                        continue;
                    }

                    value = macro.Resolve(normalized);
                }

                values[parameter.Uniform] = (float)Math.Clamp(value, parameter.Min, parameter.Max);
            }
            resolved.Add(new ResolvedEffectParameters(effect, descriptor, values));
        }
        return resolved;
    }
}
