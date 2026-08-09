import { useEffect, useState } from "react"
import { Button, Image, Slider, Text, TextField, View } from "onejs-react"
import menuStyles from "./styles/menu.uss"

compileStyleSheet(menuStyles, "project-sapphire-menu.uss")

const RuntimeCS = (globalThis as any).CS
const menuBackground = RuntimeCS.UnityEngine.Resources.Load("ProjectSapphire/main_menu_bg")
const menuFont = RuntimeCS.UnityEngine.Resources.Load("ProjectSapphire/ArialPixel")
const menuFontDefinition = RuntimeCS.UnityEngine.UIElements.FontDefinition.FromFont(menuFont)

export type ResolutionOption = {
    label: string
    index: number
}

export type MenuSettings = {
    masterVolume: number
    resolutionOptions: ResolutionOption[]
    resolutionChoice: number
    fullscreen: boolean
    vsync: boolean
    mouseSensitivity: number
    showHud: boolean
}

export function cloneMenuSettings(settings: MenuSettings): MenuSettings {
    return {
        ...settings,
        resolutionOptions: settings.resolutionOptions.map((option) => ({ ...option })),
    }
}

function PixelText({
    text,
    className,
    color,
    size,
    align,
}: {
    text: string
    className?: string
    color?: string
    size?: number
    align?: "upper-left" | "upper-center" | "upper-right" | "middle-left" | "middle-center" | "middle-right" | "lower-left" | "lower-center" | "lower-right"
}) {
    return (
        <Text
            text={text}
            className={className}
            pickingMode="Ignore"
            style={{
                unityFont: menuFont,
                unityFontDefinition: menuFontDefinition,
                color,
                fontSize: size,
                unityTextAlign: align,
                whiteSpace: "nowrap",
            }}
        />
    )
}

function MenuAction({ text, onClick, compact = false }: { text: string, onClick: () => void, compact?: boolean }) {
    return (
        <Button
            text={text}
            className={compact ? "ps-menu-action ps-menu-action--compact" : "ps-menu-action"}
            onClick={onClick}
            style={{
                unityFont: menuFont,
                unityFontDefinition: menuFontDefinition,
            }}
        />
    )
}

export function MainMenuScreen({
    onOperations,
    onMultiplayer,
    onSettings,
    onQuit,
}: {
    onOperations: () => void
    onMultiplayer: () => void
    onSettings: () => void
    onQuit: () => void
}) {
    return (
        <View name="project-sapphire-main-menu" className="ps-main-menu">
            <Image image={menuBackground} scaleMode="ScaleAndCrop" className="ps-menu-background" pickingMode="Ignore" />
            <View className="ps-menu-shade" pickingMode="Ignore" />
            <View className="ps-menu-nav">
                <View className="ps-menu-logo" pickingMode="Ignore">
                    <PixelText text="PROJECT" className="ps-menu-logo-part" />
                    <PixelText text="SAPPHIRE" className="ps-menu-logo-part ps-orange" />
                </View>
                <View className="ps-menu-actions">
                    <MenuAction text="OPERATIONS" onClick={onOperations} />
                    <MenuAction text="MULTIPLAYER" onClick={onMultiplayer} />
                    <MenuAction text="SETTINGS" onClick={onSettings} />
                    <MenuAction text="QUIT" onClick={onQuit} />
                </View>
            </View>
            <PixelText text="TACTICAL OPERATIONS SYSTEM  //  BUILD 01" className="ps-menu-build" />
        </View>
    )
}

export function LoadingScreen({ text = "LOADING..." }: { text?: string }) {
    const [shown, setShown] = useState(false)
    const [dotCount, setDotCount] = useState(0)
    const baseText = text.replace(/\.{2,3}$/, "") || "LOADING"

    useEffect(() => {
        const fadeFrame = requestAnimationFrame(() => setShown(true))
        const dotsTimer = setInterval(() => {
            setDotCount((count) => (count + 1) % 4)
        }, 375)

        return () => {
            cancelAnimationFrame(fadeFrame)
            clearInterval(dotsTimer)
        }
    }, [])

    return (
        <View
            name="project-sapphire-loading"
            className={shown ? "ps-loading-overlay ps-loading-overlay--shown" : "ps-loading-overlay"}
        >
            <View className="ps-loading-copy" pickingMode="Ignore">
                <PixelText text={baseText} className="ps-loading-text" />
                <PixelText text={".".repeat(dotCount)} className="ps-loading-dots" />
            </View>
        </View>
    )
}

export function MultiplayerPanel({
    joinCode,
    alphaBotCount,
    bravoBotCount,
    durationMinutes,
    hostTeam,
    joinTeam,
    matchMap,
    error,
    onJoinCodeChange,
    onAlphaBotCountChange,
    onBravoBotCountChange,
    onDurationMinutesChange,
    onHostTeamChange,
    onJoinTeamChange,
    onMatchMapChange,
    onHost,
    onJoin,
    onClose,
}: {
    joinCode: string
    alphaBotCount: number
    bravoBotCount: number
    durationMinutes: number
    hostTeam: number
    joinTeam: number
    matchMap: number
    error: string
    onJoinCodeChange: (code: string) => void
    onAlphaBotCountChange: (count: number) => void
    onBravoBotCountChange: (count: number) => void
    onDurationMinutesChange: (minutes: number) => void
    onHostTeamChange: (team: number) => void
    onJoinTeamChange: (team: number) => void
    onMatchMapChange: (map: number) => void
    onHost: () => void
    onJoin: () => void
    onClose: () => void
}) {
    const alphaBotLimit = hostTeam === 1 ? 3 : 4
    const bravoBotLimit = hostTeam === 2 ? 3 : 4

    return (
        <View name="project-sapphire-multiplayer" className="ps-overlay ps-multiplayer-overlay">
            <View className="ps-settings-dismiss" onClick={onClose} />
            <View className="ps-multiplayer-panel">
                <View className="ps-settings-header">
                    <View className="ps-title-mark" pickingMode="Ignore" />
                    <View className="ps-settings-heading" pickingMode="Ignore">
                        <PixelText text="UNITY RELAY" className="ps-kicker" />
                        <PixelText text="MULTIPLAYER OPERATION" className="ps-settings-title" />
                    </View>
                    <Button text="×" className="ps-settings-close" onClick={onClose} />
                </View>
                <View className="ps-multiplayer-body">
                    <View className="ps-multiplayer-section">
                        <PixelText text="HOST" className="ps-section-title" />
                        <PixelText
                            text="Set bots independently per team. Your selected team reserves one of its four slots for you."
                            className="ps-multiplayer-copy"
                        />
                        <MatchChoiceRow label="MAP">
                            <StateButton text="DUST2" active={matchMap === 0} onClick={() => onMatchMapChange(0)} />
                            <StateButton text="OFFICE" active={matchMap === 1} onClick={() => onMatchMapChange(1)} />
                        </MatchChoiceRow>
                        <MatchChoiceRow label="YOUR TEAM">
                            <StateButton text="ALPHA" active={hostTeam === 1} onClick={() => onHostTeamChange(1)} />
                            <StateButton text="BRAVO" active={hostTeam === 2} onClick={() => onHostTeamChange(2)} />
                        </MatchChoiceRow>
                        <NumberChoiceRow
                            label="ALPHA BOTS"
                            value={alphaBotCount}
                            suffix={`MAX ${alphaBotLimit}`}
                            onDecrease={() => onAlphaBotCountChange(Math.max(0, alphaBotCount - 1))}
                            onIncrease={() => onAlphaBotCountChange(Math.min(alphaBotLimit, alphaBotCount + 1))}
                        />
                        <NumberChoiceRow
                            label="BRAVO BOTS"
                            value={bravoBotCount}
                            suffix={`MAX ${bravoBotLimit}`}
                            onDecrease={() => onBravoBotCountChange(Math.max(0, bravoBotCount - 1))}
                            onIncrease={() => onBravoBotCountChange(Math.min(bravoBotLimit, bravoBotCount + 1))}
                        />
                        <NumberChoiceRow
                            label="TIME LIMIT"
                            value={durationMinutes}
                            suffix="MIN"
                            onDecrease={() => onDurationMinutesChange(Math.max(1, durationMinutes - 1))}
                            onIncrease={() => onDurationMinutesChange(Math.min(60, durationMinutes + 1))}
                        />
                        <MatchChoiceRow label="LOADOUT">
                            <PixelText text="STANDARD ISSUE // SLOT 01" className="ps-multiplayer-copy" />
                        </MatchChoiceRow>
                        <Button text="HOST OPERATION" className="ps-multiplayer-primary" onClick={onHost} />
                    </View>
                    <View className="ps-multiplayer-divider" />
                    <View className="ps-multiplayer-section">
                        <PixelText text="JOIN" className="ps-section-title" />
                        <PixelText text="Enter the host's session code and choose a side." className="ps-multiplayer-copy" />
                        <MatchChoiceRow label="YOUR TEAM">
                            <StateButton text="ALPHA" active={joinTeam === 1} onClick={() => onJoinTeamChange(1)} />
                            <StateButton text="BRAVO" active={joinTeam === 2} onClick={() => onJoinTeamChange(2)} />
                        </MatchChoiceRow>
                        <TextField
                            value={joinCode}
                            className="ps-join-code"
                            onChange={(event) => onJoinCodeChange(String(event.value).toUpperCase())}
                            style={{
                                unityFont: menuFont,
                                unityFontDefinition: menuFontDefinition,
                            }}
                        />
                        <Button text="JOIN OPERATION" className="ps-multiplayer-primary" onClick={onJoin} />
                    </View>
                    {error && <PixelText text={error} className="ps-multiplayer-error" />}
                </View>
            </View>
        </View>
    )
}

function MatchChoiceRow({
    label,
    children,
}: {
    label: string
    children: any
}) {
    return (
        <View style={{ height: 44, flexDirection: "row", alignItems: "center" }}>
            <View style={{ width: 150 }}>
                <PixelText text={label} className="ps-kicker" />
            </View>
            <View style={{ flexDirection: "row", alignItems: "center" }}>{children}</View>
        </View>
    )
}

function NumberChoiceRow({
    label,
    value,
    suffix,
    onDecrease,
    onIncrease,
}: {
    label: string
    value: number
    suffix: string
    onDecrease: () => void
    onIncrease: () => void
}) {
    return (
        <MatchChoiceRow label={label}>
            <Button text="-" className="ps-state-button" onClick={onDecrease} />
            <View style={{ width: 116, alignItems: "center" }}>
                <PixelText text={`${value} ${suffix}`} className="ps-multiplayer-copy" />
            </View>
            <Button text="+" className="ps-state-button" onClick={onIncrease} />
        </MatchChoiceRow>
    )
}

export function PauseMenuOverlay({
    onResume,
    onSettings,
    onMainMenu,
    onQuit,
}: {
    onResume: () => void
    onSettings: () => void
    onMainMenu: () => void
    onQuit: () => void
}) {
    return (
        <View name="project-sapphire-pause" className="ps-overlay">
            <View className="ps-pause-shade" pickingMode="Ignore" />
            <View className="ps-pause-card">
                <PixelText text="OPERATION SUSPENDED" className="ps-kicker" />
                <PixelText text="PAUSED" className="ps-pause-title" />
                <View className="ps-pause-actions">
                    <MenuAction text="RESUME" onClick={onResume} compact />
                    <MenuAction text="SETTINGS" onClick={onSettings} compact />
                    <MenuAction text="MAIN MENU" onClick={onMainMenu} compact />
                    <MenuAction text="QUIT" onClick={onQuit} compact />
                </View>
                <PixelText text="ESC  //  RESUME" className="ps-pause-hint" />
            </View>
        </View>
    )
}

type Tab = "audio" | "video" | "controls" | "game"

function TabButton({ tab, activeTab, onSelect }: { tab: Tab, activeTab: Tab, onSelect: (tab: Tab) => void }) {
    return (
        <Button
            text={tab.toUpperCase()}
            className={activeTab === tab ? "ps-settings-tab ps-settings-tab--active" : "ps-settings-tab"}
            onClick={() => onSelect(tab)}
        />
    )
}

function SectionHeading({ text }: { text: string }) {
    return (
        <View className="ps-section-heading" pickingMode="Ignore">
            <PixelText text={text.toUpperCase()} className="ps-section-title" />
            <View className="ps-section-line" />
        </View>
    )
}

function SettingLabel({ text }: { text: string }) {
    return <PixelText text={text} className="ps-setting-label" />
}

function StateButton({ text, active, onClick }: { text: string, active?: boolean, onClick: () => void }) {
    return (
        <Button
            text={text}
            className={active ? "ps-state-button ps-state-button--active" : "ps-state-button"}
            onClick={onClick}
        />
    )
}

function AudioTab({ draft, onChange }: { draft: MenuSettings, onChange: (next: MenuSettings) => void }) {
    return (
        <View className="ps-settings-section">
            <SectionHeading text="Audio output" />
            <View className="ps-setting-row">
                <SettingLabel text="Master volume" />
                <Slider
                    className="ps-slider"
                    lowValue={0}
                    highValue={100}
                    value={draft.masterVolume}
                    fill
                    onChange={(event) => onChange({ ...draft, masterVolume: event.value })}
                />
                <PixelText text={`${Math.round(draft.masterVolume)}`} className="ps-setting-value" />
            </View>
        </View>
    )
}

function VideoTab({ draft, onChange }: { draft: MenuSettings, onChange: (next: MenuSettings) => void }) {
    const options = draft.resolutionOptions.length > 0
        ? draft.resolutionOptions
        : [{ label: "1920 x 1080", index: 0 }]
    const currentChoice = Math.max(0, Math.min(draft.resolutionChoice, options.length - 1))
    const chooseResolution = (offset: number) => {
        const nextChoice = (currentChoice + offset + options.length) % options.length
        onChange({ ...draft, resolutionChoice: nextChoice })
    }

    return (
        <View className="ps-settings-section">
            <SectionHeading text="Display" />
            <View className="ps-setting-row">
                <SettingLabel text="Resolution" />
                <View className="ps-resolution-control">
                    <Button text="‹" className="ps-arrow-button" onClick={() => chooseResolution(-1)} />
                    <PixelText text={options[currentChoice].label} className="ps-resolution-value" />
                    <Button text="›" className="ps-arrow-button" onClick={() => chooseResolution(1)} />
                </View>
            </View>
            <View className="ps-setting-row">
                <SettingLabel text="Display mode" />
                <View className="ps-state-group">
                    <StateButton text="WINDOWED" active={!draft.fullscreen} onClick={() => onChange({ ...draft, fullscreen: false })} />
                    <StateButton text="FULLSCREEN" active={draft.fullscreen} onClick={() => onChange({ ...draft, fullscreen: true })} />
                </View>
            </View>
            <View className="ps-setting-row">
                <SettingLabel text="Vertical sync" />
                <StateButton text={draft.vsync ? "ON" : "OFF"} active={draft.vsync} onClick={() => onChange({ ...draft, vsync: !draft.vsync })} />
            </View>
        </View>
    )
}

function ControlsTab({ draft, onChange }: { draft: MenuSettings, onChange: (next: MenuSettings) => void }) {
    return (
        <View className="ps-settings-section">
            <SectionHeading text="Look controls" />
            <View className="ps-setting-row">
                <SettingLabel text="Mouse sensitivity" />
                <Slider
                    className="ps-slider"
                    lowValue={0.001}
                    highValue={10}
                    value={draft.mouseSensitivity}
                    fill
                    onChange={(event) => onChange({ ...draft, mouseSensitivity: event.value })}
                />
                <PixelText text={draft.mouseSensitivity.toFixed(2)} className="ps-setting-value" />
            </View>
        </View>
    )
}

function GameTab({ draft, onChange }: { draft: MenuSettings, onChange: (next: MenuSettings) => void }) {
    return (
        <View className="ps-settings-section">
            <SectionHeading text="Interface" />
            <View className="ps-setting-row">
                <SettingLabel text="Tactical HUD" />
                <StateButton text={draft.showHud ? "VISIBLE" : "HIDDEN"} active={draft.showHud} onClick={() => onChange({ ...draft, showHud: !draft.showHud })} />
            </View>
        </View>
    )
}

function FooterButton({ text, variant, onClick }: { text: string, variant: "muted" | "outline" | "primary", onClick: () => void }) {
    return <Button text={text} className={`ps-footer-button ps-footer-button--${variant}`} onClick={onClick} />
}

export function SettingsPanel({
    draft,
    defaults,
    onDraftChange,
    onApply,
    onClose,
}: {
    draft: MenuSettings
    defaults: MenuSettings
    onDraftChange: (next: MenuSettings) => void
    onApply: (next: MenuSettings) => void
    onClose: () => void
}) {
    const [activeTab, setActiveTab] = useState<Tab>("audio")
    const confirm = () => {
        onApply(draft)
        onClose()
    }

    return (
        <View name="project-sapphire-settings" className="ps-overlay ps-settings-overlay">
            <View className="ps-settings-dismiss" onClick={onClose} />
            <View className="ps-settings-panel">
                <View className="ps-settings-header">
                    <View className="ps-title-mark" pickingMode="Ignore" />
                    <View className="ps-settings-heading" pickingMode="Ignore">
                        <PixelText text="SYSTEM CONFIGURATION" className="ps-kicker" />
                        <PixelText text="SETTINGS" className="ps-settings-title" />
                    </View>
                    <Button text="×" className="ps-settings-close" onClick={onClose} />
                </View>
                <View className="ps-settings-body">
                    <View className="ps-settings-tabs">
                        <TabButton tab="audio" activeTab={activeTab} onSelect={setActiveTab} />
                        <TabButton tab="video" activeTab={activeTab} onSelect={setActiveTab} />
                        <TabButton tab="controls" activeTab={activeTab} onSelect={setActiveTab} />
                        <TabButton tab="game" activeTab={activeTab} onSelect={setActiveTab} />
                    </View>
                    <View className="ps-settings-content">
                        {activeTab === "audio" && <AudioTab draft={draft} onChange={onDraftChange} />}
                        {activeTab === "video" && <VideoTab draft={draft} onChange={onDraftChange} />}
                        {activeTab === "controls" && <ControlsTab draft={draft} onChange={onDraftChange} />}
                        {activeTab === "game" && <GameTab draft={draft} onChange={onDraftChange} />}
                    </View>
                </View>
                <View className="ps-settings-footer">
                    <FooterButton text="RESET" variant="muted" onClick={() => onDraftChange(cloneMenuSettings(defaults))} />
                    <FooterButton text="APPLY" variant="outline" onClick={() => onApply(draft)} />
                    <FooterButton text="OK" variant="primary" onClick={confirm} />
                </View>
            </View>
        </View>
    )
}
