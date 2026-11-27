plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

// Upload credentials are supplied only to the build process. Never store them in this repository.
val uploadNames = listOf("HUB_UPLOAD_STORE_FILE", "HUB_UPLOAD_STORE_PASSWORD", "HUB_UPLOAD_KEY_ALIAS", "HUB_UPLOAD_KEY_PASSWORD")
val uploadValues = uploadNames.associateWith { providers.environmentVariable(it).orNull?.takeIf(String::isNotBlank) }
val hasUploadKey = uploadValues.values.all { it != null }
if (uploadValues.values.any { it != null } && !hasUploadKey) {
    throw GradleException("Upload signing requires all four HUB_UPLOAD_* environment variables.")
}
if (providers.gradleProperty("hubRequireSigning").orNull == "true" && !hasUploadKey) {
    throw GradleException("A signed release was requested but the upload key is not configured.")
}

android {
    namespace = "com.minadesk.workstationhub"
    compileSdk = 36

    defaultConfig {
        applicationId = "com.minadesk.workstationhub"
        minSdk = 26
        targetSdk = 36
        versionCode = 901
        versionName = "0.9.1"
        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
        buildConfigField("String", "PRIVACY_POLICY_URL", "\"https://github.com/feed-mina/Workstation_Hub/blob/mobile/docs/privacy-android.md\"")
        buildConfigField("String", "SUPPORT_URL", "\"https://github.com/feed-mina/Workstation_Hub/issues\"")
    }

    buildFeatures { buildConfig = true }
    if (hasUploadKey) {
        signingConfigs.create("upload") {
            storeFile = file(uploadValues.getValue("HUB_UPLOAD_STORE_FILE")!!)
            if (!storeFile!!.isFile) throw GradleException("The upload keystore does not exist.")
            storePassword = uploadValues.getValue("HUB_UPLOAD_STORE_PASSWORD")
            keyAlias = uploadValues.getValue("HUB_UPLOAD_KEY_ALIAS")
            keyPassword = uploadValues.getValue("HUB_UPLOAD_KEY_PASSWORD")
        }
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
            if (hasUploadKey) signingConfig = signingConfigs.getByName("upload")
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
    androidTestImplementation("androidx.test:runner:1.7.0")
    androidTestImplementation("androidx.test.ext:junit:1.3.0")
}
