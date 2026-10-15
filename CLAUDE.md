# MühenKit Elektrik — proje rehberi

Elektrik proje çizimi (DXF CAD) ve mühendislik hesapları. Dört platform, hepsi native, ortak C++ çekirdek.
Kullanıcı Türk elektrik mühendisi; iletişim **Türkçe**. Kod yorumları ve arayüz metinleri Türkçe.

## Yapı

| Klasör | İçerik |
|---|---|
| `core/mkcad` | C++17 DXF motoru: okuma, sahne (çizgi/dolgu/yazı tamponları), düzenleme, geri al, blok/sembol, yazma. C arayüzü `include/mkcad.h` |
| `core/mkcalc` | C++ hesap çekirdeği: `mc_calc(modül, "anahtar=değer" satırları)`, `mc_table(ad)`. Tüm platformlar aynı formülleri çalıştırır |
| `core/content` | Ortak içerik: `symbols.dat` (sembol kütüphanesi), `guides.json` (rehberler), `img/`, `brand/` (simgeler), `blank.dxf` |
| `android/` | Kotlin + Jetpack Compose. JNI köprüsü `app/src/main/cpp`. Çizim OpenGL (`cad/CadGlView.kt`) |
| `windows/MuhenKit` | C# WinUI 3 + Win2D. Çekirdek `windows/native/x64/mkcore.dll` (P/Invoke, `Core/Native.cs`) |
| `apple/` | Tek SwiftUI kodu → iOS (16+) ve macOS (13+) hedefleri. XcodeGen (`project.yml`), çekirdek Bridging.h ile doğrudan derlenir |
| `docs/` | GitHub Pages: tanıtım + **gizlilik politikası** (https://makife.github.io/muhenkit/gizlilik.html) |
| `.github/` | CI iş akışları ve yardımcı betikler |

Platformlar arası eşdeğer dosyalar (bir özellik eklerken hepsini güncelle):
- Düzenleyici: `android/.../ui/CadEditor.kt` · `windows/.../Cad/CadEditor.cs` · `apple/.../Cad/CadEditor.swift` (Windows portu)
- Tablolar: `cad/Tables.kt` · `Core/Tables.cs` · `Core/Tables.swift`
- Modüller: `ui/modules/*.kt` · `Modules/*.cs` · `apple/.../Modules/*.swift`
- Dışa aktarma: `ui/Exporter.kt` · `Cad/Exporter.cs` (kendi vektör PDF yazıcısı) · `Cad/Exporter.swift` (CoreGraphics)

Tasarım: Android arayüzü esastır (mavi devre kartı ana sayfa, çizimde gizlenebilir üst çubuk + ipucu çubuğu +
Çiz/Düzenle/Ekle/Ayarlar sekmeleri, geniş ekranda sol ray). iOS/macOS bununla birebir aynı olmalı.

## Derleme (yerel, Windows bilgisayar)

- **Android**: `cd android && JAVA_HOME="/c/Program Files/Eclipse Adoptium/jdk-17.0.20.101-hotspot" ./gradlew assembleRelease bundleRelease`
  → APK'yı `../../MuhenKit_Native.apk` olarak kopyala (kullanıcı oradan dener). Emülatör kullanma.
- **Windows**: `bash windows/native/build_dll.sh` (ziglang ile mkcore.dll) · `cd windows/MuhenKit && PATH="$LOCALAPPDATA/dotnet-sdk:$PATH" dotnet build -c Release`
  → `bin/Release/net8.0-windows10.0.19041.0/win-x64/` içeriğini `../../MuhenKit_Windows/` klasörüne kopyala.
  `dotnet publish` KULLANMA (.pri/.xbf atlıyor, uygulama çöker).
- **iOS/macOS**: yerelde derlenemez (Mac yok) → yalnızca GitHub Actions.

Windows test bayrakları: `MuhenKit.exe <dxf>` · `--projeler` · `--modul=<RouteId>`; ortam `MUHENKIT_SELFTEST=1|2|3|e|ed|f1|f2|f3`
(sonuç `%TEMP%\muhenkit_selftest.txt`, hata günlüğü `%TEMP%\muhenkit_log.txt`).

## CI (GitHub Actions, depo makife/muhenkit)

- `android.yml`: imzalı APK + AAB (Play yükleme anahtarı secrets'tan; `VERSION_CODE` = run_number)
- `windows.yml`: mkcore.dll + WinUI derlemesi
- `apple.yml`: **macos-26 + Xcode 26** (App Store iOS 26 SDK şartı), macOS ve iOS derleme denetimi
- `testflight.yml`: iOS arşivi imzasız → `exportArchive` (otomatik dağıtım imzası, API anahtarı) → `.github/asc_upload.py`
  ile **App Store Connect API Build Uploads** (altool JWT'yi reddediyor, kullanma). Her apple/core gönderiminde çalışır.
- `asc-status.yml` (elle): App Store'daki yapıların işleme durumu/hataları (`.github/asc_status.py`)
- Derleme hataları `.github/report.sh` ile annotation olarak yazılır (günlük indirmeden okunur).

Secrets: `ASC_KEY_ID`, `ASC_ISSUER_ID`, `ASC_KEY_P8`, `APPLE_TEAM_ID`, `ANDROID_KEYSTORE_B64`, `ANDROID_KEYSTORE_PASSWORD`,
`ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASSWORD`.

Kimlikler: iOS paket `com.ergan.muhenkitelektrik` (App Store Connect uygulama no 6823229508, ad "MuhenKit Elektrik"),
macOS `com.ergan.muhenkitelektrik.mac`, Android `com.muhenkit.app`.

**Android yükleme anahtarı depo dışında**: `../IMZA_ANAHTARLARI/muhenkit-upload.jks` + `SIFRE_BENI_OKU.txt`;
yerelde `android/keystore.properties` (gitignore). Asla depoya koyma.

## Dersler

- Swift: tek satırda birden çok `@State var` olmaz; `UIView` alt sınıfında `gestureRecognizerShouldBegin` `override` ister.
- Windows'ta Python ile YAML/kaynak düzenlerken `\\` ve `\n` kaçışları bozulabiliyor; düzenleme sonrası `yaml.safe_load` ile doğrula.
- WinUI: XAML'siz Page'e Frame.Navigate çöker; Win10'da Segoe MDL2'de bazı glifler yok; `ToggleSwitch IsOn` Toggled'ı InitializeComponent sırasında tetikler.
- Uygulamalar internete bağlanmaz, izin istemez, veri toplamaz (gizlilik politikası buna dayanır) — ağ özelliği eklenirse politikayı güncelle.
- Hesaplar ön tasarım amaçlıdır; Hakkında ekranında sorumluluk notu var.
