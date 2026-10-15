import SwiftUI

private let fmt = Calc.fmt

/// Yükleme cetveli (Android LoadSchedule.kt / Windows LoadSchedule.cs ile aynı çekirdek girdileri)
struct LoadScheduleModule: View {
    /// Linye: tür L aydınlatma, P priz, O özel, M motor; faz R/S/T ya da RST
    struct Linye: Identifiable {
        let id = UUID()
        var name: String, type: String, count: String, unitW: String, length: String, phase: String
    }

    private static let typeNames = ["L": "Aydınlatma", "P": "Priz", "O": "Özel", "M": "Motor"]

    @State private var ls: [Linye] = [
        Linye(name: "L1", type: "L", count: "6", unitW: "100", length: "15", phase: "R"),
        Linye(name: "L2", type: "L", count: "6", unitW: "100", length: "15", phase: "S"),
        Linye(name: "P1", type: "P", count: "5", unitW: "300", length: "15", phase: "T"),
        Linye(name: "P2", type: "P", count: "5", unitW: "300", length: "15", phase: "R"),
        Linye(name: "P3", type: "O", count: "1", unitW: "2000", length: "10", phase: "S"),
    ]
    @State private var three = true
    @State private var demandMode = 0
    @State private var demandFactor = "0.6"
    @State private var kolonL = "15", limit = "1.5", cos = "0.8"

    private func input(_ balance: Bool) -> [(String, Any?)] {
        var m: [(String, Any?)] = [("three", three), ("demand_mode", demandMode), ("demand_factor", demandFactor), ("kolon_l", kolonL),
                                   ("limit", limit), ("cos", cos), ("n", ls.count), ("balance", balance)]
        for (i, l) in ls.enumerated() {
            m += [("linye.\(i).name", l.name), ("linye.\(i).type", l.type), ("linye.\(i).count", l.count),
                  ("linye.\(i).unit_w", l.unitW), ("linye.\(i).length", l.length), ("linye.\(i).phase", three ? l.phase : "R")]
        }
        return m
    }

    private func add(_ t: String) {
        let prefix = t == "L" ? "L" : t == "M" ? "M" : "P"
        let n = ls.filter { $0.name.hasPrefix(prefix) }.count + 1
        let name = "\(prefix)\(n)"
        switch t {
        case "L": ls.append(Linye(name: name, type: t, count: "6", unitW: "100", length: "15", phase: "R"))
        case "P": ls.append(Linye(name: name, type: t, count: "5", unitW: "300", length: "15", phase: "R"))
        case "O": ls.append(Linye(name: name, type: t, count: "1", unitW: "2000", length: "10", phase: "R"))
        default: ls.append(Linye(name: name, type: t, count: "1", unitW: "4000", length: "10", phase: three ? "RST" : "R"))
        }
    }

    private func balance() {
        let b = Calc.run("load_schedule", input(true))
        for i in ls.indices { let ph = b["linye.\(i).phase"]; if !ph.isEmpty { ls[i].phase = ph } }
    }

    var body: some View {
        let r = Calc.run("load_schedule", input(false))
        let worst = r.i("worst")
        ModulePage(route: .load, subtitle: "Linyeler, faz dağılımı, sigorta/kablo seçimi, gerilim düşümü") {
            MCard("Pano") {
                ChoiceField(nil, [true, false], selection: $three) { $0 ? "Trifaze pano" : "Monofaze pano" }
                ChoiceField("Talep gücü", [0, 1], selection: $demandMode) { $0 == 0 ? "Konut %60/%40" : "Sabit katsayı" }
                if demandMode == 1 { NumField("Talep faktörü", text: $demandFactor) }
                FieldRow { NumField("Kolon uzunluğu", "m", text: $kolonL); NumField("%e sınırı", "%", text: $limit); NumField("cos φ (kolon)", text: $cos) }
            }
            MCard("Linyeler") {
                ForEach($ls) { $l in
                    let i = ls.firstIndex { $0.id == l.id } ?? 0
                    let p = "linye.\(i)."
                    VStack(alignment: .leading, spacing: 4) {
                        HStack(alignment: .bottom, spacing: 8) {
                            VStack(alignment: .leading) {
                                Text(l.name).font(.system(size: 15, weight: .semibold))
                                Note(Self.typeNames[l.type] ?? "")
                            }
                            .frame(width: 70, alignment: .leading)
                            NumField(l.type == "L" ? "Sorti" : "Adet", text: $l.count)
                            NumField("Birim güç", "W", text: $l.unitW)
                            NumField("Uzunluk", "m", text: $l.length)
                            if three {
                                let phases = l.type == "M" ? ["RST"] : ["R", "S", "T", "RST"]
                                VStack(alignment: .leading, spacing: 4) {
                                    Text("Faz").font(.caption).foregroundStyle(Theme.muted)
                                    Picker("", selection: $l.phase) { ForEach(phases, id: \.self) { Text($0).tag($0) } }
                                        .labelsHidden().pickerStyle(.menu)
                                }
                                .frame(width: 80, alignment: .leading)
                            }
                            Button { ls.removeAll { $0.id == l.id } } label: { Image(systemName: "trash") }
                                .buttonStyle(.borderless)
                                .padding(.bottom, 8)
                        }
                        Text("\(fmt(r.d(p + "power"), 0)) W · \(fmt(r.d(p + "current"), 2)) A · \(r[p + "fuse"]) A sigorta · \(r[p + "cable"]) · %e \(fmt(r.d(p + "drop"), 3))")
                            .font(.footnote.weight(.semibold)).foregroundStyle(Theme.accent)
                        Divider().overlay(Color.white.opacity(0.08))
                    }
                }
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(spacing: 8) {
                        ForEach(["L", "P", "O", "M"], id: \.self) { t in
                            Button("+ " + (Self.typeNames[t] ?? t)) { add(t) }.buttonStyle(.bordered)
                        }
                        if three { Button("Fazları dengele", action: balance).buttonStyle(.borderedProminent) }
                    }
                }
            }
        } right: {
            MCard("Sonuç") {
                ResultRow("Kurulu güç", fmt(r.d("installed"), 0), "W")
                ResultRow("Talep gücü", fmt(r.d("demand"), 0), "W", big: true)
                ResultRow("Pano akımı", fmt(r.d("board_current"), 2), "A")
                ResultRow("Ana sigorta", r["main_fuse"], "A", big: true)
                ResultRow("Kolon kablosu", r["kolon.name"].isEmpty ? "sınırı sağlayan yok" : r["kolon.name"], ok: !r["kolon.name"].isEmpty)
                ResultRow("Kolon %e", fmt(r.d("kolon_drop"), 3), "%")
                ResultRow("Toplam %e (kolon + \(worst >= 0 ? r["linye.\(worst).name"] : "-"))", fmt(r.d("total_drop"), 3), "%",
                          ok: r.d("total_drop") <= (Calc.num(limit) ?? 1.5) * 2)
                if three {
                    ResultRow("Faz yükleri R / S / T", "\(fmt(r.d("phase_r"), 0)) / \(fmt(r.d("phase_s"), 0)) / \(fmt(r.d("phase_t"), 0))", "W")
                    ResultRow("Dengesizlik", fmt(r.d("unbalance"), 1), "%", ok: r.d("unbalance") <= 20)
                }
                ResultRow("Kaçak akım rölesi", r["kakr"])
            }
        }
    }
}
