import SwiftUI

/// Ana sayfa kartı → modül ekranı
struct ModuleView: View {
    let route: RouteId

    var body: some View {
        switch route {
        case .lighting: LightingModule()
        case .load: LoadScheduleModule()
        case .drop: VoltageDropModule()
        case .diversity: DiversityModule()
        case .grounding: GroundingModule()
        case .lightning: LightningModule()
        case .motor: MotorModule()
        case .cable: CableModule()
        case .meter: MeterModule()
        case .lux: LuxModule()
        case .materials: MaterialsModule()
        case .weak: WeakModule()
        case .smm: SmmModule()
        case .cad, .projects: EmptyView()
        }
    }
}
