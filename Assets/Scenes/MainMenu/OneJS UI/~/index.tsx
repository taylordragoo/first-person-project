import { useState } from "react"
import { render, View } from "onejs-react"
import {
    cloneMenuSettings,
    MainMenuScreen,
    MultiplayerPanel,
    SettingsPanel,
    type MenuSettings,
} from "./menus"
import {
    applySettings,
    readSettings,
} from "./settings-bridge"

const RuntimeCS = (globalThis as any).CS

function App() {
    const [showSettings, setShowSettings] = useState(false)
    const [showMultiplayer, setShowMultiplayer] = useState(false)
    const [joinCode, setJoinCode] = useState("")
    const [multiplayerError, setMultiplayerError] = useState("")
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
        RuntimeCS.UnityEngine.SceneManagement.SceneManager.LoadScene("OperationsDemo")
    }

    const startMultiplayerHost = () => {
        if (!__isPlaying) return
        setMultiplayerError("")
        RuntimeCS.FirstPersonProject.UI.ProjectSapphireBridge.EnterGameplayMode()
        const launched = RuntimeCS.FPSProject.Multiplayer.Core.Bootstrap.MultiplayerMenuBridge.LaunchHost()
        if (!launched) {
            RuntimeCS.FirstPersonProject.UI.ProjectSapphireBridge.EnterMenuMode()
            setMultiplayerError(String(RuntimeCS.FPSProject.Multiplayer.Core.Bootstrap.MultiplayerMenuBridge.LastError))
        }
    }

    const startMultiplayerClient = () => {
        if (!__isPlaying) return
        setMultiplayerError("")
        const bridge = RuntimeCS.FPSProject.Multiplayer.Core.Bootstrap.MultiplayerMenuBridge
        RuntimeCS.FirstPersonProject.UI.ProjectSapphireBridge.EnterGameplayMode()
        const launched = bridge.LaunchClient(joinCode)
        if (!launched) {
            RuntimeCS.FirstPersonProject.UI.ProjectSapphireBridge.EnterMenuMode()
            setMultiplayerError(String(bridge.LastError))
        }
    }

    const quit = () => {
        if (!__isPlaying) return
        RuntimeCS.UnityEngine.Application.Quit()
    }

    return (
        <View style={{ position: "absolute", top: 0, right: 0, bottom: 0, left: 0 }}>
            <MainMenuScreen
                onOperations={startOperations}
                onMultiplayer={() => {
                    setMultiplayerError("")
                    setShowMultiplayer(true)
                }}
                onSettings={openSettings}
                onQuit={quit}
            />
            {showMultiplayer && (
                <MultiplayerPanel
                    joinCode={joinCode}
                    error={multiplayerError}
                    onJoinCodeChange={setJoinCode}
                    onHost={startMultiplayerHost}
                    onJoin={startMultiplayerClient}
                    onClose={() => setShowMultiplayer(false)}
                />
            )}
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
