import { useEffect, useState } from "react"
import { Button, render, Text, useFrameSync, useThrottledSync, View } from "onejs-react"
import {
    cloneMenuSettings,
    PauseMenuOverlay,
    SettingsPanel,
    type MenuSettings,
} from "./menus"
import { applySettings, readSettings } from "./settings-bridge"
import {
    decodeWeaponHudSnapshot,
    readWeaponHudSnapshot,
    type WeaponHudData,
} from "./weapon-bridge"

const colors = {
    cyan: "rgba(34, 211, 238, 1)",
    cyanBright: "rgba(114, 235, 246, 1)",
    cyanLine: "rgba(34, 211, 238, 0.62)",
    cyanSoft: "rgba(34, 211, 238, 0.18)",
    cyanFaint: "rgba(34, 211, 238, 0.055)",
    orange: "rgba(249, 115, 22, 1)",
    orangeSoft: "rgba(249, 115, 22, 0.55)",
    green: "rgba(74, 222, 128, 1)",
    yellow: "rgba(234, 179, 8, 1)",
    white: "rgba(244, 254, 255, 1)",
    dark: "rgba(0, 8, 12, 0.72)",
    darkSolid: "rgba(2, 12, 16, 0.96)",
    slate: "rgba(21, 42, 49, 0.92)",
}

const ignore = { pickingMode: "Ignore" as const }

function HudText({
    name,
    text,
    size = 14,
    color = colors.cyan,
    tracking = 1,
    align = "middle-left" as const,
}: {
    name?: string
    text: string
    size?: number
    color?: string
    tracking?: number
    align?: "upper-left" | "upper-center" | "upper-right" | "middle-left" | "middle-center" | "middle-right" | "lower-left" | "lower-center" | "lower-right"
}) {
    return (
        <Text
            {...ignore}
            name={name}
            text={text}
            style={{
                color,
                fontSize: size,
                letterSpacing: tracking,
                whiteSpace: "nowrap",
                unityTextAlign: align,
                unityFontStyleAndWeight: "bold",
                unityTextOutlineColor: "rgba(0, 24, 30, 0.82)",
                unityTextOutlineWidth: 0.35,
            }}
        />
    )
}

function VisorRails() {
    const rail = (side: "left" | "right") => {
        const sideStyle = side === "left" ? { left: 43 } : { right: 43 }
        const capStyle = side === "left" ? { left: 43 } : { right: 43 }

        return (
            <View {...ignore} key={side} style={{ position: "absolute", top: 0, right: 0, bottom: 0, left: 0 }}>
                <View
                    {...ignore}
                    style={{
                        position: "absolute",
                        ...sideStyle,
                        top: 48,
                        bottom: 48,
                        width: 2,
                        backgroundColor: colors.cyanLine,
                    }}
                />
                <View
                    {...ignore}
                    style={{
                        position: "absolute",
                        ...capStyle,
                        top: 48,
                        width: 52,
                        height: 2,
                        backgroundColor: colors.cyanLine,
                    }}
                />
                <View
                    {...ignore}
                    style={{
                        position: "absolute",
                        ...capStyle,
                        bottom: 48,
                        width: 52,
                        height: 2,
                        backgroundColor: colors.cyanLine,
                    }}
                />
            </View>
        )
    }

    return <>{rail("left")}{rail("right")}</>
}

function Corner({ horizontal, vertical }: { horizontal: "left" | "right", vertical: "top" | "bottom" }) {
    const x = horizontal === "left" ? { left: 0 } : { right: 0 }
    const y = vertical === "top" ? { top: 0 } : { bottom: 0 }

    return (
        <View {...ignore} style={{ position: "absolute", ...x, ...y, width: 28, height: 22 }}>
            <View
                {...ignore}
                style={{
                    position: "absolute",
                    ...x,
                    ...y,
                    width: 28,
                    height: 4,
                    backgroundColor: colors.orange,
                }}
            />
            <View
                {...ignore}
                style={{
                    position: "absolute",
                    ...x,
                    ...y,
                    width: 4,
                    height: 22,
                    backgroundColor: colors.orange,
                }}
            />
        </View>
    )
}

const buildings = [
    { left: 12, width: 30, height: 74 },
    { left: 48, width: 45, height: 92 },
    { left: 99, width: 28, height: 66 },
    { left: 133, width: 52, height: 108 },
    { left: 192, width: 36, height: 80 },
    { left: 234, width: 28, height: 112 },
    { left: 268, width: 32, height: 70 },
]

function CityFeed() {
    return (
        <View
            {...ignore}
            style={{
                position: "absolute",
                top: 9,
                right: 9,
                bottom: 9,
                left: 9,
                overflow: "hidden",
                backgroundColor: colors.darkSolid,
                borderWidth: 1,
                borderColor: colors.cyanLine,
            }}
        >
            <View
                {...ignore}
                style={{ position: "absolute", top: 0, right: 0, left: 0, height: 84, backgroundColor: colors.slate }}
            />
            <View
                {...ignore}
                style={{
                    position: "absolute",
                    top: 21,
                    right: 44,
                    width: 31,
                    height: 31,
                    borderRadius: 16,
                    backgroundColor: "rgba(125, 180, 184, 0.16)",
                }}
            />
            <View
                {...ignore}
                style={{ position: "absolute", right: 0, bottom: 70, left: 0, height: 42, backgroundColor: "rgba(12, 31, 36, 0.96)" }}
            />
            {buildings.map((building, index) => (
                <View
                    {...ignore}
                    key={`building-${index}`}
                    style={{
                        position: "absolute",
                        left: building.left,
                        bottom: 0,
                        width: building.width,
                        height: building.height,
                        backgroundColor: index % 2 === 0 ? "rgba(4, 13, 17, 1)" : "rgba(7, 18, 22, 1)",
                    }}
                />
            ))}
            <View
                {...ignore}
                style={{ position: "absolute", right: 0, bottom: 0, left: 0, height: 60, backgroundColor: "rgba(0, 4, 7, 0.56)" }}
            />
            <View {...ignore} style={{ position: "absolute", right: 12, bottom: 10, left: 12 }}>
                <HudText text="FEED 01  //  LIVE" size={9} color="rgba(114, 235, 246, 0.88)" tracking={1.7} />
                <View {...ignore} style={{ height: 3 }} />
                <HudText text="GHOST SQUAD" size={22} color={colors.white} tracking={2.4} />
            </View>
        </View>
    )
}

function FeedFrame() {
    return (
        <View
            {...ignore}
            style={{
                position: "relative",
                width: 312,
                height: 190,
                backgroundColor: "rgba(0, 8, 12, 0.44)",
            }}
        >
            <CityFeed />
            <Corner horizontal="left" vertical="top" />
            <Corner horizontal="right" vertical="top" />
            <Corner horizontal="left" vertical="bottom" />
            <Corner horizontal="right" vertical="bottom" />
        </View>
    )
}

function StatusSquare({ active = false }: { active?: boolean }) {
    return (
        <View
            {...ignore}
            style={{
                width: 20,
                height: 20,
                flexShrink: 0,
                backgroundColor: active ? colors.green : "rgba(0, 0, 0, 0.18)",
                borderWidth: active ? 0 : 2,
                borderColor: colors.green,
            }}
        />
    )
}

function SquadList() {
    return (
        <View {...ignore} style={{ width: 272, marginTop: 8 }}>
            <View {...ignore} style={{ height: 38, flexDirection: "row", alignItems: "center" }}>
                <StatusSquare active />
                <View
                    {...ignore}
                    style={{
                        position: "relative",
                        width: 232,
                        height: 36,
                        marginLeft: 10,
                        paddingRight: 8,
                        paddingLeft: 10,
                        flexDirection: "row",
                        alignItems: "center",
                        justifyContent: "space-between",
                        backgroundColor: "rgba(8, 48, 58, 0.58)",
                        borderWidth: 2,
                        borderColor: colors.cyan,
                    }}
                >
                    <HudText text="PAUL SMITH" size={17} tracking={1.2} />
                    <View {...ignore} style={{ width: 16, height: 16, borderWidth: 2, borderColor: colors.yellow }} />
                </View>
            </View>
            {[0, 1].map((index) => (
                <View {...ignore} key={`empty-${index}`} style={{ height: 31, paddingTop: 7 }}>
                    <StatusSquare />
                </View>
            ))}
        </View>
    )
}

function CrossCom() {
    return (
        <View
            {...ignore}
            style={{
                position: "absolute",
                top: 48,
                left: 64,
                width: 312,
                alignItems: "center",
            }}
        >
            <HudText text="GO TO" size={31} tracking={4} align="middle-center" />
            <View {...ignore} style={{ height: 7 }} />
            <FeedFrame />
            <View {...ignore} style={{ height: 6 }} />
            <HudText text="REGROUP" size={27} tracking={3.4} align="middle-center" />
            <SquadList />
        </View>
    )
}

function SoldierIcon() {
    return (
        <View {...ignore} style={{ width: 54, height: 54, alignItems: "center", justifyContent: "center" }}>
            <View {...ignore} style={{ position: "absolute", top: 4, left: 22, width: 11, height: 11, borderRadius: 6, backgroundColor: colors.cyan }} />
            <View {...ignore} style={{ position: "absolute", top: 17, left: 20, width: 15, height: 20, backgroundColor: colors.cyan, rotate: -8 }} />
            <View {...ignore} style={{ position: "absolute", top: 22, left: 31, width: 22, height: 5, backgroundColor: colors.cyan }} />
            <View {...ignore} style={{ position: "absolute", top: 35, left: 16, width: 7, height: 18, backgroundColor: colors.cyan, rotate: 18 }} />
            <View {...ignore} style={{ position: "absolute", top: 35, left: 31, width: 7, height: 19, backgroundColor: colors.cyan, rotate: -24 }} />
        </View>
    )
}

const pulseSegments = [
    { left: 3, top: 34, width: 20, rotate: 0 },
    { left: 20, top: 27, width: 21, rotate: -49 },
    { left: 34, top: 31, width: 32, rotate: 68 },
    { left: 57, top: 34, width: 24, rotate: -48 },
    { left: 76, top: 34, width: 22, rotate: 29 },
    { left: 95, top: 38, width: 34, rotate: 0 },
]

function VitalsGraph() {
    const gridLines = Array.from({ length: 7 })
    return (
        <View {...ignore} style={{ position: "relative", width: 132, height: 72, overflow: "hidden", backgroundColor: colors.dark }}>
            {gridLines.map((_, index) => (
                <View
                    {...ignore}
                    key={`vgrid-v-${index}`}
                    style={{ position: "absolute", top: 0, bottom: 0, left: index * 20, width: 1, backgroundColor: colors.cyanSoft }}
                />
            ))}
            {Array.from({ length: 4 }).map((_, index) => (
                <View
                    {...ignore}
                    key={`vgrid-h-${index}`}
                    style={{ position: "absolute", right: 0, left: 0, top: index * 20, height: 1, backgroundColor: colors.cyanSoft }}
                />
            ))}
            {pulseSegments.map((segment, index) => (
                <View
                    {...ignore}
                    key={`pulse-${index}`}
                    style={{
                        position: "absolute",
                        left: segment.left,
                        top: segment.top,
                        width: segment.width,
                        height: 2,
                        rotate: segment.rotate,
                        transformOrigin: [0, "50%"],
                        backgroundColor: colors.green,
                    }}
                />
            ))}
        </View>
    )
}

function PlayerStatus() {
    return (
        <View
            {...ignore}
            style={{
                position: "absolute",
                left: 64,
                bottom: 52,
                height: 88,
                padding: 6,
                flexDirection: "row",
                backgroundColor: "rgba(8, 75, 92, 0.24)",
                borderWidth: 2,
                borderColor: colors.cyan,
            }}
        >
            <View
                {...ignore}
                style={{
                    position: "relative",
                    width: 76,
                    height: 72,
                    marginRight: 5,
                    alignItems: "center",
                    justifyContent: "center",
                    overflow: "hidden",
                    backgroundColor: colors.dark,
                    borderWidth: 1,
                    borderColor: colors.cyanLine,
                }}
            >
                <SoldierIcon />
                <View {...ignore} style={{ position: "absolute", top: 5, right: 5, width: 6, height: 6, backgroundColor: colors.cyan }} />
            </View>
            <View {...ignore} style={{ borderWidth: 1, borderColor: colors.cyanLine }}>
                <VitalsGraph />
            </View>
        </View>
    )
}

function Bullet({ index, active, color }: { index: number, active: boolean, color: string }) {
    return (
        <View {...ignore} style={{ width: 8, height: 43, marginLeft: index === 0 ? 0 : 4, alignItems: "center" }}>
            <View
                {...ignore}
                style={{
                    width: 4,
                    height: 7,
                    borderTopLeftRadius: 2,
                    borderTopRightRadius: 2,
                    backgroundColor: active ? color : "rgba(34, 211, 238, 0.16)",
                }}
            />
            <View
                {...ignore}
                style={{
                    width: 7,
                    height: 31,
                    backgroundColor: active ? color : "rgba(34, 211, 238, 0.16)",
                }}
            />
        </View>
    )
}

function AmmoCounter({ weapon }: { weapon: WeaponHudData }) {
    const isHeat = weapon.mode === "heat"
    const isBallistic = weapon.mode === "ballistic"
    const isInfinite = weapon.mode === "infinite"
    const isMelee = weapon.mode === "melee"
    const empty = isBallistic && weapon.currentAmmo <= 0
    const warning = weapon.lowAmmo || empty
    const accent = warning ? colors.orange : colors.cyan
    const ratio = isHeat
        ? weapon.heatRatio
        : isBallistic && weapon.magazineSize > 0
            ? weapon.currentAmmo / weapon.magazineSize
            : weapon.available
                ? 1
                : 0
    const activeSegments = Math.max(0, Math.min(5, ratio <= 0 ? 0 : Math.ceil(ratio * 5)))
    const primaryValue = !weapon.available || isMelee
        ? "--"
        : isInfinite
            ? "∞"
            : isHeat
                ? `${Math.round(weapon.heatRatio * 100)}`
                : `${weapon.currentAmmo}`
    const secondaryValue = isBallistic
        ? `${weapon.reserveAmmo}`
        : isInfinite
            ? "∞"
            : isHeat
                ? "HEAT"
                : "--"
    const status = !weapon.available
        ? "OFFLINE"
        : weapon.reloading
            ? "RELOADING"
            : empty
                ? "EMPTY"
                : weapon.lowAmmo
                    ? "LOW AMMO"
                    : isHeat
                        ? "THERMAL"
                        : isMelee
                            ? "MELEE"
                            : "READY"

    return (
        <View
            {...ignore}
            style={{
                position: "absolute",
                right: 64,
                bottom: 52,
                width: 300,
                height: 118,
                backgroundColor: colors.dark,
                borderWidth: 2,
                borderColor: accent,
            }}
        >
            <View
                {...ignore}
                style={{
                    height: 29,
                    paddingRight: 9,
                    paddingLeft: 9,
                    flexDirection: "row",
                    alignItems: "center",
                    borderBottomWidth: 1,
                    borderBottomColor: accent,
                    backgroundColor: "rgba(8, 48, 58, 0.68)",
                }}
            >
                <HudText text={weapon.slot > 0 ? `WPN 0${weapon.slot}` : "WPN --"} size={9} color={accent} tracking={1.5} />
                <View {...ignore} style={{ width: 9 }} />
                <View {...ignore} style={{ flexGrow: 1, overflow: "hidden" }}>
                    <HudText name="project-sapphire-weapon-name" text={weapon.weaponName} size={12} color={colors.white} tracking={1.2} />
                </View>
                <HudText name="project-sapphire-weapon-status" text={status} size={8} color={accent} tracking={1.2} align="middle-right" />
            </View>
            <View {...ignore} style={{ height: 85, flexDirection: "row" }}>
                <View {...ignore} style={{ width: 142, borderRightWidth: 2, borderRightColor: accent }}>
                    <View {...ignore} style={{ height: 57, alignItems: "center", justifyContent: "center", backgroundColor: "rgba(8, 48, 58, 0.50)" }}>
                        <HudText name="project-sapphire-ammo-current" text={primaryValue} size={41} color={accent} tracking={0} align="middle-center" />
                    </View>
                    <View {...ignore} style={{ height: 26, paddingRight: 10, paddingLeft: 10, flexDirection: "row", alignItems: "center", justifyContent: "space-between", backgroundColor: accent }}>
                        <HudText text={isHeat ? "LEVEL" : "RES"} size={8} color="rgba(3, 16, 19, 0.78)" tracking={1.5} />
                        <HudText name="project-sapphire-ammo-reserve" text={secondaryValue} size={17} color="rgba(3, 16, 19, 1)" tracking={1.6} align="middle-right" />
                    </View>
                </View>
                <View {...ignore} style={{ flexGrow: 1, paddingTop: 9, paddingRight: 10, paddingBottom: 8, paddingLeft: 10 }}>
                    <View {...ignore} style={{ height: 47, flexDirection: "row", alignItems: "center", justifyContent: "center" }}>
                        {[0, 1, 2, 3, 4].map((index) => (
                            <Bullet key={index} index={index} active={index < activeSegments} color={accent} />
                        ))}
                    </View>
                    <View {...ignore} style={{ height: 20, flexDirection: "row", alignItems: "center", justifyContent: "space-between" }}>
                        <HudText text={isHeat ? "CORE" : isMelee ? "TYPE" : "MAG"} size={8} color={colors.cyanBright} tracking={1.3} />
                        <HudText
                            name="project-sapphire-magazine-size"
                            text={isHeat ? `${Math.round(weapon.heatRatio * 100)}%` : isMelee ? "CQC" : weapon.magazineSize > 0 ? `${weapon.magazineSize}` : "--"}
                            size={10}
                            color={accent}
                            tracking={1.4}
                            align="middle-right"
                        />
                    </View>
                </View>
            </View>
        </View>
    )
}

function Reticle() {
    return (
        <View {...ignore} style={{ position: "absolute", top: "50%", right: 0, left: 0, height: 12, flexDirection: "row", alignItems: "center", justifyContent: "center", translate: [0, -6] }}>
            <View {...ignore} style={{ width: 24, height: 1, marginRight: 5, backgroundColor: "rgba(234, 179, 8, 0.62)" }} />
            <View {...ignore} style={{ width: 5, height: 5, borderRadius: 3, backgroundColor: colors.yellow }} />
        </View>
    )
}

function Chevron({ offset }: { offset: number }) {
    return (
        <View {...ignore} style={{ position: "relative", width: 76, height: 82, marginRight: 16, marginLeft: 16, translate: [0, offset] }}>
            <View {...ignore} style={{ position: "absolute", top: 16, left: 4, width: 48, height: 3, rotate: 63, transformOrigin: [0, "50%"], backgroundColor: colors.cyan }} />
            <View {...ignore} style={{ position: "absolute", top: 58, right: 4, width: 48, height: 3, rotate: -63, transformOrigin: ["100%", "50%"], backgroundColor: colors.cyan }} />
            <View {...ignore} style={{ position: "absolute", top: 22, left: 17, width: 31, height: 1, rotate: 63, transformOrigin: [0, "50%"], backgroundColor: colors.cyanLine }} />
            <View {...ignore} style={{ position: "absolute", top: 49, right: 17, width: 31, height: 1, rotate: -63, transformOrigin: ["100%", "50%"], backgroundColor: colors.cyanLine }} />
        </View>
    )
}

function CenterMarkers() {
    return (
        <View {...ignore} style={{ position: "absolute", top: 0, right: 0, bottom: 0, left: 0 }}>
            <Reticle />
            <View {...ignore} style={{ position: "absolute", right: 0, bottom: 130, left: 0, height: 110, flexDirection: "row", alignItems: "center", justifyContent: "center" }}>
                <Chevron offset={-14} />
                <Chevron offset={0} />
                <Chevron offset={-28} />
            </View>
            <View
                {...ignore}
                style={{
                    position: "absolute",
                    top: "50%",
                    right: "20%",
                    width: 39,
                    height: 57,
                    paddingTop: 13,
                    paddingRight: 7,
                    paddingBottom: 10,
                    paddingLeft: 7,
                    borderWidth: 1,
                    borderColor: colors.cyanLine,
                    backgroundColor: "rgba(34, 211, 238, 0.10)",
                    translate: [0, -29],
                }}
            >
                {[0, 1, 2].map((index) => (
                    <View {...ignore} key={`signal-${index}`} style={{ width: 23, height: 2, marginBottom: 7, backgroundColor: colors.cyan }} />
                ))}
            </View>
        </View>
    )
}

function TacticalHud() {
    return (
        <View
            name="project-sapphire-hud"
            {...ignore}
            style={{
                position: "absolute",
                top: 0,
                right: 0,
                bottom: 0,
                left: 0,
                overflow: "hidden",
            }}
        >
            <VisorRails />
            <CrossCom />
            <PlayerStatus />
            <CenterMarkers />
        </View>
    )
}

const RuntimeCS = (globalThis as any).CS
function isPaused(): boolean {
    if (!__isPlaying) return false

    try {
        return Boolean(RuntimeCS.FirstPersonProject.UI.ProjectSapphireBridge.PollPauseState())
    } catch {
        return false
    }
}

type MultiplayerSessionData = {
    active: boolean
    state: string
    joinCode: string
    error: string
    players: number
    host: boolean
}

const offlineSession: MultiplayerSessionData = {
    active: false,
    state: "OFFLINE",
    joinCode: "",
    error: "",
    players: 0,
    host: false,
}

function readMultiplayerSessionSnapshot(): string {
    if (!__isPlaying) return JSON.stringify(offlineSession)

    try {
        return String(RuntimeCS.FPSProject.Multiplayer.Core.Bootstrap.MultiplayerMenuBridge.ReadSessionSnapshot())
    } catch {
        return JSON.stringify(offlineSession)
    }
}

function decodeMultiplayerSessionSnapshot(snapshot: string): MultiplayerSessionData {
    try {
        return { ...offlineSession, ...JSON.parse(snapshot) }
    } catch {
        return offlineSession
    }
}

function LoadingScreen({ text }: { text: string }) {
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

    const textStyle = {
        color: "rgb(255, 107, 0)",
        fontSize: 20,
        letterSpacing: 6,
        whiteSpace: "nowrap" as const,
        unityFontStyleAndWeight: "bold" as const,
    }

    return (
        <View
            name="project-sapphire-loading"
            className={shown ? "ps-loading-overlay ps-loading-overlay--shown" : "ps-loading-overlay"}
        >
            <View className="ps-loading-copy" pickingMode="Ignore">
                <Text text={baseText} pickingMode="Ignore" style={textStyle} />
                <Text
                    text={".".repeat(dotCount)}
                    pickingMode="Ignore"
                    style={{ ...textStyle, width: 42, letterSpacing: 2 }}
                />
            </View>
        </View>
    )
}

function MultiplayerSessionPanel({ session }: { session: MultiplayerSessionData }) {
    if (!session.active) return null

    const copyCode = () => {
        RuntimeCS.FPSProject.Multiplayer.Core.Bootstrap.MultiplayerMenuBridge.CopyJoinCode()
    }

    return (
        <View
            name="multiplayer-session-status"
            style={{
                position: "absolute",
                top: 28,
                right: 58,
                width: 310,
                paddingTop: 13,
                paddingRight: 15,
                paddingBottom: 13,
                paddingLeft: 15,
                borderLeftWidth: 2,
                borderLeftColor: session.error ? "rgb(255, 104, 78)" : colors.orange,
                backgroundColor: colors.darkSolid,
            }}
        >
            <HudText text="MULTIPLAYER // UNITY RELAY" size={10} color={colors.orange} tracking={1.6} />
            <HudText text={session.state} size={16} color={session.error ? "rgb(255, 104, 78)" : colors.white} tracking={1.3} />
            {session.joinCode && (
                <>
                    <HudText text={`JOIN CODE  ${session.joinCode}`} size={20} color={colors.cyanBright} tracking={2.2} />
                    {session.host && (
                        <Button
                            text="COPY JOIN CODE"
                            onClick={copyCode}
                            style={{
                                height: 30,
                                marginTop: 7,
                                borderWidth: 1,
                                borderColor: colors.cyanLine,
                                borderRadius: 0,
                                backgroundColor: colors.cyanSoft,
                                color: colors.cyanBright,
                                fontSize: 10,
                                letterSpacing: 1.1,
                            }}
                        />
                    )}
                </>
            )}
            {session.players > 0 && (
                <HudText text={`PLAYERS  ${session.players}`} size={10} color={colors.green} tracking={1.2} />
            )}
            {session.error && (
                <Text
                    text={session.error}
                    pickingMode="Ignore"
                    style={{
                        marginTop: 6,
                        color: "rgb(255, 150, 130)",
                        fontSize: 10,
                        whiteSpace: "normal",
                    }}
                />
            )}
        </View>
    )
}

function App() {
    const paused = useFrameSync(isPaused, [])
    const weaponSnapshot = useThrottledSync(readWeaponHudSnapshot, 50)
    const weaponHud = decodeWeaponHudSnapshot(weaponSnapshot)
    const sessionSnapshot = useThrottledSync(readMultiplayerSessionSnapshot, 100)
    const multiplayerSession = decodeMultiplayerSessionSnapshot(sessionSnapshot)
    const [showSettings, setShowSettings] = useState(false)
    const [draft, setDraft] = useState<MenuSettings>(() => readSettings())
    const [defaults, setDefaults] = useState<MenuSettings>(() => readSettings(true))
    const [showHud, setShowHud] = useState(() => readSettings().showHud)
    const loadingText = multiplayerSession.state === "LOADING"
        ? "LOADING OPERATION..."
        : multiplayerSession.state === "STARTING RELAY HOST"
            ? "ESTABLISHING RELAY..."
            : multiplayerSession.state === "JOINING RELAY SESSION"
                ? "JOINING OPERATION..."
                : ""

    useEffect(() => {
        if (!paused) setShowSettings(false)
    }, [paused])

    const openSettings = () => {
        setDraft(readSettings())
        setDefaults(readSettings(true))
        setShowSettings(true)
    }

    const saveSettings = (next: MenuSettings) => {
        applySettings(next)
        setDraft(cloneMenuSettings(next))
        setShowHud(next.showHud)
    }

    const resume = () => {
        if (!__isPlaying) return
        RuntimeCS.FirstPersonProject.UI.ProjectSapphireBridge.SetPaused(false)
    }

    const returnToMainMenu = () => {
        if (!__isPlaying) return
        RuntimeCS.FirstPersonProject.UI.ProjectSapphireBridge.EnterMenuMode()
        RuntimeCS.FPSProject.Multiplayer.Core.Bootstrap.MultiplayerMenuBridge.ReturnToMainMenu()
    }

    const quit = () => {
        if (!__isPlaying) return
        RuntimeCS.UnityEngine.Application.Quit()
    }

    return (
        <View style={{ position: "absolute", top: 0, right: 0, bottom: 0, left: 0 }}>
            {showHud && (
                <>
                    <TacticalHud />
                    <AmmoCounter weapon={weaponHud} />
                </>
            )}
            <MultiplayerSessionPanel session={multiplayerSession} />
            {paused && !showSettings && (
                <PauseMenuOverlay
                    onResume={resume}
                    onSettings={openSettings}
                    onMainMenu={returnToMainMenu}
                    onQuit={quit}
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
    console.log("[OneJS UI] Tactical HUD and menu overlay started")
}

export function onStop() {
    if (__isPlaying) RuntimeCS.FirstPersonProject.UI.ProjectSapphireBridge.SetPaused(false)
    console.log("[OneJS UI] Tactical HUD and menu overlay stopped")
}

render(<App />, __root)
