allprojects {
    repositories {
        google()
        mavenCentral()
    }
}

val newBuildDir: Directory =
    rootProject.layout.buildDirectory
        .dir("../../build")
        .get()
rootProject.layout.buildDirectory.value(newBuildDir)

subprojects {
    val newSubprojectBuildDir: Directory = newBuildDir.dir(project.name)
    project.layout.buildDirectory.value(newSubprojectBuildDir)
    // Force all plugin modules (e.g. flutter_webrtc) to compile against a modern SDK.
    afterEvaluate {
        extensions.findByName("android")?.let { ext ->
            try {
                val setter = ext.javaClass.getMethod("compileSdkVersion", Int::class.javaPrimitiveType)
                setter.invoke(ext, 36)
            } catch (_: Exception) {
            }
        }
    }
}
subprojects {
    project.evaluationDependsOn(":app")
}

tasks.register<Delete>("clean") {
    delete(rootProject.layout.buildDirectory)
}
