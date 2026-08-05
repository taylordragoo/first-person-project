import type { MenuSettings, ResolutionOption } from "./menus"

const RuntimeCS = (globalThis as any).CS
const itemSeparator = "\u001e"

const fallbackResolutions: ResolutionOption[] = [
    { label: "1280 x 720", index: 0 },
    { label: "1600 x 900", index: 1 },
    { label: "1920 x 1080", index: 2 },
    { label: "2560 x 1440", index: 3 },
]

function runtimeBridge(): any | null {
    try {
        return RuntimeCS.FirstPersonProject.UI.ProjectSapphireBridge
    } catch {
        return null
    }
}

function resolutionOptions(bridge: any | null): ResolutionOption[] {
    if (!bridge) return fallbackResolutions.map((option) => ({ ...option }))

    try {
        const labels = String(bridge.GetResolutionOptions())
            .split(itemSeparator)
            .filter((label) => label.length > 0)
        return labels.map((label, index) => ({ label, index }))
    } catch {
        return fallbackResolutions.map((option) => ({ ...option }))
    }
}

export function readSettings(useDefaults = false): MenuSettings {
    const bridge = runtimeBridge()
    const options = resolutionOptions(bridge)
    const resolutionChoice = bridge
        ? Math.max(0, Math.min(options.length - 1, Number(bridge.GetResolutionChoice(useDefaults))))
        : Math.min(2, options.length - 1)

    return {
        masterVolume: bridge ? Number(bridge.GetMasterVolume(useDefaults)) : 80,
        resolutionOptions: options,
        resolutionChoice,
        fullscreen: bridge ? Boolean(bridge.GetFullscreen(useDefaults)) : true,
        vsync: bridge ? Boolean(bridge.GetVSync(useDefaults)) : true,
        mouseSensitivity: bridge ? Number(bridge.GetMouseSensitivity(useDefaults)) : 1,
        showHud: bridge ? Boolean(bridge.GetShowHud(useDefaults)) : true,
    }
}

export function applySettings(settings: MenuSettings) {
    const bridge = runtimeBridge()
    const resolution = settings.resolutionOptions[settings.resolutionChoice]
    if (!bridge) return

    bridge.ApplySettings(
        settings.masterVolume,
        resolution?.index ?? settings.resolutionChoice,
        settings.fullscreen,
        settings.vsync,
        settings.mouseSensitivity,
        settings.showHud,
    )
}