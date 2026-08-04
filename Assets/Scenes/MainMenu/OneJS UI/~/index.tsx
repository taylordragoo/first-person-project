import { useState } from "react"
import { render, View } from "onejs-react"
import {
    cloneMenuSettings,
    MainMenuScreen,
    SettingsPanel,
    type MenuSettings,
} from "../../../../FortressLevel/OutDoorScene_URP_OneJS/OneJS UI/~/menus"
import {
    applySettings,
    readSettings,
} from "../../../../FortressLevel/OutDoorScene_URP_OneJS/OneJS UI/~/settings-bridge"

const RuntimeCS = (globalThis as any).CS

function App() {
    const [showSettings, setShowSettings] = useState(false)
    const [draft, setDraft] = useState<MenuSettings>(() => readSettings())
    const [defaults, setDefaults] = useState<MenuSettings>(() => readSettings(true))

    const openSettings = () => {
        setDraft(readSettings())
        setDefaults(readSettings(true))
        setShowSettings(true)
    }

    const saveSettings = (next: MenuSettings) => {
        applySettings(next)
        setDraft(cloneMenuSettings(next))
    }

    const startOperations = () => {
        if (!__isPlaying) return
        RuntimeCS.FirstPersonProject.UI.ProjectSapphireBridge.EnterGameplayMode()
        RuntimeCS.UnityEngine.SceneManagement.SceneManager.LoadScene("OutDoorScene_URP_OneJS")
    }

    const quit = () => {
        if (!__isPlaying) return
        RuntimeCS.UnityEngine.Application.Quit()
    }

    return (
        <View style={{ position: "absolute", top: 0, right: 0, bottom: 0, left: 0 }}>
            <MainMenuScreen onOperations={startOperations} onSettings={openSettings} onQuit={quit} />
            {showSettings && (
                <SettingsPanel
                    draft={draft}
                    defaults={defaults}
                    onDraftChange={setDraft}
                    onApply={saveSettings}
                    onClose={() => setShowSettings(false)}
                />
            )}
        </View>
    )
}

export function onPlay() {
    RuntimeCS.FirstPersonProject.UI.ProjectSapphireBridge.EnterMenuMode()
    console.log("[OneJS UI] Project Sapphire main menu started")
}

export function onStop() {
    console.log("[OneJS UI] Project Sapphire main menu stopped")
}

render(<App />, __root)
