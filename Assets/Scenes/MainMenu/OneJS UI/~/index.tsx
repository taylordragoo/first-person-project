import { useState } from "react"
import { render, View } from "onejs-react"
import {
    cloneMenuSettings,
    LoadingScreen,
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
    const [alphaBotCount, setAlphaBotCount] = useState(3)
    const [bravoBotCount, setBravoBotCount] = useState(4)
    const [durationMinutes, setDurationMinutes] = useState(5)
    const [hostTeam, setHostTeam] = useState(1)
    const [joinTeam, setJoinTeam] = useState(1)
    const [matchMap, setMatchMap] = useState(0)
    const [multiplayerError, setMultiplayerError] = useState("")
    const [loadingText, setLoadingText] = useState("")
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
        setLoadingText("LOADING OPERATION...")
        RuntimeCS.FirstPersonProject.UI.ProjectSapphireBridge.EnterGameplayMode()
        RuntimeCS.UnityEngine.SceneManagement.SceneManager.LoadScene("OperationsDemo")
    }

    const startMultiplayerHost = () => {
        if (!__isPlaying) return
        setMultiplayerError("")
        setLoadingText("ESTABLISHING RELAY...")
        RuntimeCS.FirstPersonProject.UI.ProjectSapphireBridge.EnterGameplayMode()
        const launched = RuntimeCS.FPSProject.Multiplayer.Core.Bootstrap.MultiplayerMenuBridge.LaunchHost(
            alphaBotCount,
            bravoBotCount,
            durationMinutes,
            hostTeam,
            matchMap,
        )
        if (!launched) {
            RuntimeCS.FirstPersonProject.UI.ProjectSapphireBridge.EnterMenuMode()
            setLoadingText("")
            setMultiplayerError(String(RuntimeCS.FPSProject.Multiplayer.Core.Bootstrap.MultiplayerMenuBridge.LastError))
        }
    }

    const changeHostTeam = (team: number) => {
        setHostTeam(team)
        if (team === 1) {
            setAlphaBotCount((count) => Math.min(3, count))
        } else if (team === 2) {
            setBravoBotCount((count) => Math.min(3, count))
        }
    }

    const startMultiplayerClient = () => {
        if (!__isPlaying) return
        setMultiplayerError("")
        setLoadingText("JOINING OPERATION...")
        const bridge = RuntimeCS.FPSProject.Multiplayer.Core.Bootstrap.MultiplayerMenuBridge
        RuntimeCS.FirstPersonProject.UI.ProjectSapphireBridge.EnterGameplayMode()
        const launched = bridge.LaunchClient(joinCode, joinTeam)
        if (!launched) {
            RuntimeCS.FirstPersonProject.UI.ProjectSapphireBridge.EnterMenuMode()
            setLoadingText("")
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
                    alphaBotCount={alphaBotCount}
                    bravoBotCount={bravoBotCount}
                    durationMinutes={durationMinutes}
                    hostTeam={hostTeam}
                    joinTeam={joinTeam}
                    matchMap={matchMap}
                    error={multiplayerError}
                    onJoinCodeChange={setJoinCode}
                    onAlphaBotCountChange={setAlphaBotCount}
                    onBravoBotCountChange={setBravoBotCount}
                    onDurationMinutesChange={setDurationMinutes}
                    onHostTeamChange={changeHostTeam}
                    onJoinTeamChange={setJoinTeam}
                    onMatchMapChange={setMatchMap}
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
            {loadingText && <LoadingScreen text={loadingText} />}
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
