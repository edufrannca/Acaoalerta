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
        versionCode = (project.findProperty("alerta.versionCode") as String?)?.toIntOrNull() ?: 2
        versionName = "0.2-demo"
        // Valores padrão dos campos de configuração (podem ser alterados no app). A chave do gateway NUNCA é embutida por padrão.
        buildConfigField("String", "URL_BACKEND", "\"${project.findProperty("alerta.url") ?: ""}\"")
        buildConfigField("String", "RECEPTOR", "\"${project.findProperty("alerta.receptor") ?: "gateway-android"}\"")
        buildConfigField("String", "CHAVE_GATEWAY", "\"\"")
    }
    buildFeatures { buildConfig = true }
    compileOptions { sourceCompatibility = JavaVersion.VERSION_17; targetCompatibility = JavaVersion.VERSION_17 }
    kotlinOptions { jvmTarget = "17" }
    testOptions { unitTests.isReturnDefaultValues = true }
}

dependencies {
    implementation("com.google.android.gms:play-services-nearby:19.3.0")
    implementation("androidx.appcompat:appcompat:1.7.0")
    implementation("androidx.core:core-ktx:1.13.1")
    testImplementation("junit:junit:4.13.2")
    // org.json real nos testes JVM (o android.jar só tem stubs).
    testImplementation("org.json:json:20240303")
}
