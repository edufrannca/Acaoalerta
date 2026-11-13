plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

android {
    namespace = "br.alertaemacao.nearby"
    compileSdk = 35
    defaultConfig {
        applicationId = "br.alertaemacao.nearby"
        minSdk = 26
        targetSdk = 35
        versionCode = 1
        versionName = "0.1-poc"
        // URL do backend demonstrativo e chave do gateway (derivada no servidor: HMAC(segredo, "gateway:<receptor>")).
        buildConfigField("String", "URL_BACKEND", "\"${project.findProperty("alerta.url") ?: "http://10.0.2.2:8080"}\"")
        buildConfigField("String", "RECEPTOR", "\"${project.findProperty("alerta.receptor") ?: "gateway-poc"}\"")
        buildConfigField("String", "CHAVE_GATEWAY", "\"${project.findProperty("alerta.chaveGateway") ?: ""}\"")
    }
    buildFeatures { buildConfig = true }
    compileOptions { sourceCompatibility = JavaVersion.VERSION_17; targetCompatibility = JavaVersion.VERSION_17 }
    kotlinOptions { jvmTarget = "17" }
}

dependencies {
    implementation("com.google.android.gms:play-services-nearby:19.3.0")
    implementation("androidx.appcompat:appcompat:1.7.0")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.9.0")
}
