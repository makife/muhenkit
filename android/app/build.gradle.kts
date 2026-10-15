import java.util.Properties

plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
}

android {
    namespace = "com.muhenkit.app"
    compileSdk = 36
    ndkVersion = "28.2.13676358"

    defaultConfig {
        applicationId = "com.muhenkit.app"
        minSdk = 26
        targetSdk = 36
        // CI'da her derleme artan numara (Play aynı numarayı ikinci kez kabul etmez)
        versionCode = (System.getenv("VERSION_CODE") ?: "1").toInt()
        versionName = "2.0.0"
        ndk { abiFilters += listOf("arm64-v8a", "armeabi-v7a", "x86_64") }
    }

    // ortak C++ çekirdek (core/mkcad) + JNI köprüsü → libmuhenkit.so
    externalNativeBuild {
        cmake {
            path = file("src/main/cpp/CMakeLists.txt")
            version = "3.22.1"
        }
    }

    // Play yükleme anahtarı: bilgisayarda keystore.properties (depoya girmez), CI'da ortam değişkenleri.
    // İkisi de yoksa debug anahtarıyla imzalanır.
    val ks = Properties().apply {
        val f = rootProject.file("keystore.properties")
        if (f.exists()) f.inputStream().use { load(it) }
    }
    val ksFile = System.getenv("ANDROID_KEYSTORE_FILE") ?: ks.getProperty("storeFile")
    signingConfigs {
        if (ksFile != null && file(ksFile).exists()) {
            create("upload") {
                storeFile = file(ksFile)
                storePassword = System.getenv("ANDROID_KEYSTORE_PASSWORD") ?: ks.getProperty("storePassword")
                keyAlias = System.getenv("ANDROID_KEY_ALIAS") ?: ks.getProperty("keyAlias")
                keyPassword = System.getenv("ANDROID_KEY_PASSWORD") ?: ks.getProperty("keyPassword")
            }
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            signingConfig = signingConfigs.findByName("upload") ?: signingConfigs.getByName("debug")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    buildFeatures { compose = true }

    // ortak içerik (rehber metinleri, görseller): tüm platformlar core/content'i kullanır
    sourceSets {
        getByName("main") { assets.srcDirs("src/main/assets", "../../core/content") }
    }
}

kotlin {
    compilerOptions { jvmTarget.set(org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17) }
}

dependencies {
    val bom = platform("androidx.compose:compose-bom:2026.06.01")
    implementation(bom)
    implementation("androidx.core:core-ktx:1.18.0")
    implementation("androidx.activity:activity-compose:1.13.0")
    implementation("androidx.lifecycle:lifecycle-runtime-ktx:2.10.0")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.11.0")
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.foundation:foundation")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.material:material-icons-extended:1.7.8")
    testImplementation("junit:junit:4.13.2")
}
