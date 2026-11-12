#if os(iOS)
import UIKit

/// Boş yere dokununca klavye kapanır ve imleç söner. Pencereye tek dokunuş tanıyıcı eklenir
/// (sayfalar/sheet'ler de aynı pencerede); dokunuşları engellemez, yazı alanlarına dokunuşu yok sayar.
final class KeyboardDismisser: NSObject, UIGestureRecognizerDelegate {
    static let shared = KeyboardDismisser()
    private var windows = Set<ObjectIdentifier>()

    func install() {
        for case let scene as UIWindowScene in UIApplication.shared.connectedScenes {
            for w in scene.windows where !windows.contains(ObjectIdentifier(w)) {
                let tap = UITapGestureRecognizer(target: self, action: #selector(tapped(_:)))
                tap.cancelsTouchesInView = false
                tap.delaysTouchesEnded = false
                tap.delegate = self
                w.addGestureRecognizer(tap)
                windows.insert(ObjectIdentifier(w))
            }
        }
    }

    @objc private func tapped(_ g: UITapGestureRecognizer) {
        g.view?.endEditing(true)
    }

    func gestureRecognizer(_ g: UIGestureRecognizer, shouldReceive touch: UITouch) -> Bool {
        var v = touch.view
        while let x = v {
            if x is UITextField || x is UITextView { return false }
            v = x.superview
        }
        return true
    }

    func gestureRecognizer(_ g: UIGestureRecognizer, shouldRecognizeSimultaneouslyWith other: UIGestureRecognizer) -> Bool {
        true
    }
}
#endif
