#!/usr/bin/env python3
import os
import sys
import shutil
import subprocess

def run(cmd):
    print(">>", " ".join(cmd) if isinstance(cmd, list) else cmd, flush=True)
    subprocess.run(cmd, check=True, shell=isinstance(cmd, str))

def dotnet_publish(rid, version, out_dir):
    run([
        "dotnet", "publish", "ZoteroWorkflowApp.csproj",
        "-c", "Release",
        "-r", rid,
        "--self-contained", "true",
        "-p:PublishSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:IncludeAllContentForSelfExtract=true",
        "-p:EnableCompressionInSingleFile=true",
        f"-p:Version={version}",
        f"-p:AssemblyVersion={version}.0",
        f"-p:FileVersion={version}.0",
        "-o", out_dir
    ])

def build_win_and_linux(version):
    os.makedirs("release-assets", exist_ok=True)

    # 1. Windows .exe
    dotnet_publish("win-x64", version, "dist/win-x64")
    shutil.copy2("dist/win-x64/ZoteroWorkflowApp.exe", f"release-assets/ZoteroWorkflowApp-v{version}-win-x64.exe")

    # 2. Linux binary -> .deb & .AppImage
    dotnet_publish("linux-x64", version, "dist/linux-x64")
    linux_bin = "dist/linux-x64/ZoteroWorkflowApp"
    os.chmod(linux_bin, 0o755)

    # 2A. Build .deb
    deb_dir = f"pkg-deb/zotero-workflow-app_{version}_amd64"
    os.makedirs(f"{deb_dir}/DEBIAN", exist_ok=True)
    os.makedirs(f"{deb_dir}/usr/bin", exist_ok=True)
    os.makedirs(f"{deb_dir}/usr/share/applications", exist_ok=True)
    os.makedirs(f"{deb_dir}/usr/share/icons/hicolor/256x256/apps", exist_ok=True)

    shutil.copy2(linux_bin, f"{deb_dir}/usr/bin/zotero-workflow-app")
    os.chmod(f"{deb_dir}/usr/bin/zotero-workflow-app", 0o755)
    shutil.copy2("Assets/app.png", f"{deb_dir}/usr/share/icons/hicolor/256x256/apps/zotero-workflow-app.png")

    control_text = (
        f"Package: zotero-workflow-app\n"
        f"Version: {version}\n"
        f"Section: science\n"
        f"Priority: optional\n"
        f"Architecture: amd64\n"
        f"Depends: libc6, libstdc++6, libgtk-3-0\n"
        f"Recommends: libwebkit2gtk-4.1-0 | libwebkit2gtk-4.0-37, libsoup-3.0-0\n"
        f"Maintainer: Zotero AI Research Studio <scholar@academic.org>\n"
        f"Description: Zotero AI Research Studio - Semantic Search, Knowledge Graph & Literature Review\n"
    )
    with open(f"{deb_dir}/DEBIAN/control", "w", encoding="utf-8") as f:
        f.write(control_text)

    desktop_text = (
        "[Desktop Entry]\n"
        "Name=Zotero AI Research Studio\n"
        "Comment=Semantic Search, Knowledge Graph & Literature Review Workstation\n"
        "Exec=/usr/bin/zotero-workflow-app\n"
        "Icon=zotero-workflow-app\n"
        "Terminal=false\n"
        "Type=Application\n"
        "Categories=Education;Science;Literature;\n"
    )
    with open(f"{deb_dir}/usr/share/applications/zotero-workflow-app.desktop", "w", encoding="utf-8") as f:
        f.write(desktop_text)

    run(["dpkg-deb", "--build", "--root-owner-group", deb_dir, f"release-assets/ZoteroWorkflowApp-v{version}-linux-amd64.deb"])

    # 2B. Build .AppImage
    appdir = "pkg-appimage/ZoteroWorkflowApp.AppDir"
    os.makedirs(f"{appdir}/usr/bin", exist_ok=True)
    shutil.copy2(linux_bin, f"{appdir}/usr/bin/ZoteroWorkflowApp")
    os.chmod(f"{appdir}/usr/bin/ZoteroWorkflowApp", 0o755)
    shutil.copy2("Assets/app.png", f"{appdir}/zotero-workflow-app.png")
    shutil.copy2("Assets/app.png", f"{appdir}/.DirIcon")

    apprun_text = (
        "#!/bin/sh\n"
        'HERE="$(dirname "$(readlink -f "${0}")")"\n'
        'exec "${HERE}/usr/bin/ZoteroWorkflowApp" "$@"\n'
    )
    with open(f"{appdir}/AppRun", "w", encoding="utf-8") as f:
        f.write(apprun_text)
    os.chmod(f"{appdir}/AppRun", 0o755)

    appimage_desktop = (
        "[Desktop Entry]\n"
        "Name=Zotero AI Research Studio\n"
        "Exec=ZoteroWorkflowApp\n"
        "Icon=zotero-workflow-app\n"
        "Type=Application\n"
        "Categories=Education;Science;\n"
    )
    with open(f"{appdir}/zotero-workflow-app.desktop", "w", encoding="utf-8") as f:
        f.write(appimage_desktop)

    run(["curl", "-sL", "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage", "-o", "appimagetool"])
    os.chmod("appimagetool", 0o755)
    env = os.environ.copy()
    env["ARCH"] = "x86_64"
    subprocess.run(["./appimagetool", "--appimage-extract-and-run", appdir, f"release-assets/ZoteroWorkflowApp-v{version}-linux-x86_64.AppImage"], check=True, env=env)

def build_macos_dmg(version):
    os.makedirs("release-assets", exist_ok=True)
    os.makedirs("AppIcon.iconset", exist_ok=True)

    # Convert Assets/app.png -> app.icns
    sizes = [
        (16, "icon_16x16.png"),
        (32, "icon_16x16@2x.png"),
        (32, "icon_32x32.png"),
        (64, "icon_32x32@2x.png"),
        (128, "icon_128x128.png"),
        (256, "icon_128x128@2x.png"),
        (256, "icon_256x256.png"),
        (512, "icon_256x256@2x.png"),
    ]
    for sz, name in sizes:
        run(["sips", "-z", str(sz), str(sz), "Assets/app.png", "--out", f"AppIcon.iconset/{name}"])
    run(["iconutil", "-c", "icns", "AppIcon.iconset", "-o", "app.icns"])

    for rid, arch_label in [("osx-arm64", "arm64"), ("osx-x64", "x64")]:
        dotnet_publish(rid, version, f"dist/{rid}")

        dmg_stage = f"dmg-stage-{rid}"
        app_bundle = f"{dmg_stage}/Zotero AI Studio.app"
        os.makedirs(f"{app_bundle}/Contents/MacOS", exist_ok=True)
        os.makedirs(f"{app_bundle}/Contents/Resources", exist_ok=True)

        shutil.copy2(f"dist/{rid}/ZoteroWorkflowApp", f"{app_bundle}/Contents/MacOS/ZoteroWorkflowApp")
        os.chmod(f"{app_bundle}/Contents/MacOS/ZoteroWorkflowApp", 0o755)
        shutil.copy2("app.icns", f"{app_bundle}/Contents/Resources/app.icns")

        plist = f"""<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>
  <string>Zotero AI Studio</string>
  <key>CFBundleDisplayName</key>
  <string>Zotero AI Research Studio</string>
  <key>CFBundleIdentifier</key>
  <string>org.academic.zoteroworkflowapp</string>
  <key>CFBundleVersion</key>
  <string>{version}</string>
  <key>CFBundleShortVersionString</key>
  <string>{version}</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleExecutable</key>
  <string>ZoteroWorkflowApp</string>
  <key>CFBundleIconFile</key>
  <string>app.icns</string>
  <key>NSHighResolutionCapable</key>
  <true/>
  <key>LSMinimumSystemVersion</key>
  <string>11.0</string>
</dict>
</plist>
"""
        with open(f"{app_bundle}/Contents/Info.plist", "w", encoding="utf-8") as f:
            f.write(plist)

        run(["codesign", "--force", "--deep", "--sign", "-", app_bundle])

        out_dmg = f"release-assets/ZoteroWorkflowApp-v{version}-macos-{arch_label}.dmg"
        run([
            "hdiutil", "create",
            "-volname", f"Zotero AI Studio v{version}",
            "-srcfolder", dmg_stage,
            "-ov",
            "-format", "UDZO",
            out_dmg
        ])

if __name__ == "__main__":
    mode = sys.argv[1]
    ver = sys.argv[2]
    if mode == "win-linux":
        build_win_and_linux(ver)
    elif mode == "macos":
        build_macos_dmg(ver)
