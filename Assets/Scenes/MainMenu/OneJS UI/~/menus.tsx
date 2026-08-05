import { useState } from "react"
import { Button, Image, Slider, Text, View } from "onejs-react"
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
    onSettings,
    onQuit,
}: {
    onOperations: () => void
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
                    <MenuAction text="SETTINGS" onClick={onSettings} />
                    <MenuAction text="QUIT" onClick={onQuit} />
                </View>
            </View>
            <PixelText text="TACTICAL OPERATIONS SYSTEM  //  BUILD 01" className="ps-menu-build" />
        </View>
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