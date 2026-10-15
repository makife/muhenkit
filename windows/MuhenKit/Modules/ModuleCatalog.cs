using MuhenKit.Core;

namespace MuhenKit.Modules;

/// <summary>Ana sayfa çipi → modül kurucusu</summary>
public static class ModuleCatalog
{
    public static void Build(RouteId id, ModuleUi ui)
    {
        switch (id)
        {
            case RouteId.Lighting: Electrical.Lighting(ui); break;
            case RouteId.Load: LoadSchedule.Build(ui); break;
            case RouteId.Drop: Electrical.VoltageDrop(ui); break;
            case RouteId.Diversity: Electrical.Diversity(ui); break;
            case RouteId.Grounding: Earthing.Grounding(ui); break;
            case RouteId.Lightning: Earthing.Lightning(ui); break;
            case RouteId.Motor: Electrical.Motor(ui); break;
            case RouteId.Cable: Electrical.Cable(ui); break;
            case RouteId.Meter: DataModules.MeterPanel(ui); break;
            case RouteId.Lux: DataModules.Lux(ui); break;
            case RouteId.Materials: DataModules.Materials(ui); break;
            case RouteId.Weak: DataModules.WeakCurrent(ui); break;
            case RouteId.Smm: DataModules.Smm(ui); break;
            default: throw new InvalidOperationException("Bilinmeyen modül: " + id);
        }
    }
}
