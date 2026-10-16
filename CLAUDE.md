# MühenKit Elektrik — proje rehberi

Elektrik proje çizimi (DXF CAD) ve mühendislik hesapları. Dört platform, hepsi native, ortak C++ çekirdek.
Kullanıcı Türk elektrik mühendisi; iletişim **Türkçe**. Kod yorumları ve arayüz metinleri Türkçe.

## Yapı

| Klasör | İçerik |
|---|---|
| `core/mkcad` | C++17 DXF motoru: okuma, sahne (çizgi/dolgu/yazı tamponları), düzenleme, geri al, blok/sembol, yazma. C arayüzü `include/mkcad.h` |
| `core/mkcalc` | C++ hesap çekirdeği: `mc_calc(modül, "anahtar=değer" satırları)`, `mc_table(ad)`. Tüm platformlar aynı formülleri çalıştırır |
| `core/content` | Ortak içerik: `symbols.dat` (sembol kütüphanesi), `guides.json` (rehberler), `img/`, `brand/` (simgeler), `blank.dxf`, `demo.dxf` (Örnek proje; Windows `MUHENKIT_SELFTEST=demo` + `MUHENKIT_DEMO_OUT` ile üretilir) |
| `android/` | Kotlin + Jetpack Compose. JNI köprüsü `app/src/main/cpp`. Çizim OpenGL (`cad/CadGlView.kt`) |
| `windows/MuhenKit` | C# WinUI 3 + Win2D. Çekirdek `windows/native/x64/mkcore.dll` (P/Invoke, `Core/Native.cs`) |
| `apple/` | Tek SwiftUI kodu → iOS (16+) ve macOS (13+) hedefleri. XcodeGen (`project.yml`), çekirdek Bridging.h ile doğrudan derlenir |
| `docs/` | Gizlilik politikası kaynağı. Yayın: **Netlify** https://elegant-cannoli-ecf0d2.netlify.app (kullanıcı `docs/gizlilik.html`ı index.html olarak elle yükler); GitHub Pages kopyası da duruyor |
| `store/` | Mağaza metinleri (`metinler.md`) ve görsel üretici (`gorseller.py`) |
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

Windows test bayrakları: `MuhenKit.exe <dxf>` · `--projeler` · `--modul=<RouteId>`; ortam `MUHENKIT_SELFTEST=1|2|3|e|ed|f1|f2|f3|demo`
(sonuç `%TEMP%\muhenkit_selftest.txt`, hata günlüğü `%TEMP%\muhenkit_log.txt`).

## CI (GitHub Actions, depo makife/muhenkit)

- `android.yml`: imzalı APK + AAB (Play yükleme anahtarı secrets'tan; `VERSION_CODE` = run_number), ardından `.github/play_upload.py` ile
  Play **Kapalı test (alpha)** kanalına yükler (hizmet hesabı muhenkit-play@bielat-1c00a, secret `PLAY_SERVICE_ACCOUNT_JSON`;
  uygulama yayınlanmamışsa taslak olarak). Yerelde erişim denetimi: `python .github/play_upload.py <json> com.muhenkit.app - alpha`
- `windows.yml`: mkcore.dll + WinUI derlemesi
- `apple.yml`: **macos-26 + Xcode 26** (App Store iOS 26 SDK şartı), macOS ve iOS derleme denetimi
- `testflight.yml`: iOS arşivi imzasız → `exportArchive` (otomatik dağıtım imzası, API anahtarı) → `.github/asc_upload.py`
  ile **App Store Connect API Build Uploads** (altool JWT'yi reddediyor, kullanma). Her apple/core gönderiminde çalışır.
  Aynı dosyada `mac` işi: Mac App Store (ayrı satış) — depo değişkeni `ASC_MAC_APP_ID` = 6823622933 (.pkg, platform MAC_OS; mağaza adı "MühenKit Elektrik Mac", ilk yükleme yapı 26).
- `asc-status.yml` (elle): App Store'daki yapıların işleme durumu/hataları (`.github/asc_status.py`) — "TestFlight'ta yok" denirse önce buna bak.
- `ipad-ekran.yml` (elle): iPad Pro 13" simülatöründe mağaza görüntüleri çeker (uygulama `-mkScreen home|cad|projects|<RouteId>` açılış
  parametresiyle ekranı örnek projeyle açar); artifact `ipad-ekranlar`.
- Derleme hataları `.github/report.sh` ile annotation olarak yazılır (günlük indirmeden okunur).

Secrets: `PLAY_SERVICE_ACCOUNT_JSON`, `ASC_KEY_ID`, `ASC_ISSUER_ID`, `ASC_KEY_P8`, `APPLE_TEAM_ID`, `ANDROID_KEYSTORE_B64`, `ANDROID_KEYSTORE_PASSWORD`,
`ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASSWORD`.

Kimlikler: iOS paket `com.ergan.muhenkitelektrik` (App Store Connect uygulama no 6823229508), macOS `com.ergan.muhenkitelektrik.mac`
(ayrı kayıt, ayrı satış; Mac'te PRODUCT_NAME "MühenKit Elektrik", EXECUTABLE_NAME ASCII `MuhenKit` — "ü" codesign'ı bozuyor),
Android `com.muhenkit.app`. Uygulama adı her yerde **MühenKit Elektrik**. Destek e-postası makifergan@gmail.com.
Dört platformda ayrı ücretli satış, uygulama içi satın alma yok, sunucu yok. iPad destekli (kullanıcının tableti yok).

## Mağaza

- Metinler: `store/metinler.md` (karakter sınırları denetli). Gizlilik/destek URL: Netlify adresi (yukarıda).
- Görseller depo dışında `../MAGAZA_GORSELLERI/`: `play_one_cikan_grafik_1024x500.png`, `play_simge_512.png`, `android_telefon/`,
  `iphone_6.9/`, `ipad_13/`, `tam_aciklama.txt`. Üretim: ham görüntüleri `ham_android|ham_iphone|ham_ipad/` içine (1.png…) +
  `basliklar.txt` ("Başlık | alt başlık"), sonra `PYTHONIOENCODING=utf-8 python store/gorseller.py ekran <ham klasör>`.
  Android çıktısında durum/gezinme çubuğu, iPhone'da durum çubuğu kırpılır.
- Play: ilk AAB (`../MuhenKit_Elektrik_Play.aab`) kullanıcı tarafından elle Kapalı test (Alpha) kanalına yüklenir (API o zamana
  kadar "Package not found" der); sonrakileri android.yml otomatik yükler.
- Microsoft Store: MSIX için Partner Center "Product identity" değerleri (Name, Publisher, PublisherDisplayName) bekleniyor.

**Android yükleme anahtarı depo dışında**: `../IMZA_ANAHTARLARI/muhenkit-upload.jks` + `SIFRE_BENI_OKU.txt`;
yerelde `android/keystore.properties` (gitignore). Asla depoya koyma.

## Dersler

- Swift: tek satırda birden çok `@State var` olmaz; `UIView` alt sınıfında `gestureRecognizerShouldBegin` `override` ister.
- Windows'ta Python ile YAML/kaynak düzenlerken `\\` ve `\n` kaçışları bozulabiliyor; düzenleme sonrası `yaml.safe_load` ile doğrula.
- WinUI: XAML'siz Page'e Frame.Navigate çöker; Win10'da Segoe MDL2'de bazı glifler yok; `ToggleSwitch IsOn` Toggled'ı InitializeComponent sırasında tetikler.
- Uygulamalar internete bağlanmaz, izin istemez, veri toplamaz (gizlilik politikası buna dayanır) — ağ özelliği eklenirse politikayı güncelle.
- Hesaplar ön tasarım amaçlıdır; Hakkında ekranında sorumluluk notu var.
- App Store artık yalnız Xcode 26 / iOS 26 SDK yapıları kabul ediyor (90725); macos-15 sunucusu yetmez.
- Yerel Python çıktısında Türkçe karakter için `PYTHONIOENCODING=utf-8` kullan (konsol cp1254).
- Windows uygulaması bir dosyayı açık tutuyorsa (test penceresi) yazma "Access denied" verir: önce geçici klasöre yaz, sonra kopyala.
