plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

android {
    namespace = "com.minadesk.workstationhub"
    compileSdk = 35

    defaultConfig {
        applicationId = "com.minadesk.workstationhub"
        minSdk = 26
        targetSdk = 35
        // Windows 0.9.0.0 과 같은 기능 세대를 뜻합니다.
        versionCode = 900
        versionName = "0.9.0.0"
    }

    // 데스크톱과 같은 화면 파일(ui/)을 그대로 assets 로 포함합니다. 복사본을 두지 않습니다.
    sourceSets {
        getByName("main") {
            assets.srcDirs("src/main/assets", rootProject.file("../ui"))
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"))
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions { jvmTarget = "17" }

    packaging {
        // 데스크톱 전용 브라우저 시험용 파일은 APK 에 넣지 않습니다.
        resources.excludes += setOf("**/*.md")
    }
}

// APK runtime dependencies remain framework + Kotlin only. These libraries are used by JVM tests.
dependencies {
    testImplementation("junit:junit:4.13.2")
    testImplementation("org.json:json:20240303")
}
