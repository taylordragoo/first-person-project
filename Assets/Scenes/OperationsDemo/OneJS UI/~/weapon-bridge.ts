export type WeaponHudMode = "none" | "ballistic" | "infinite" | "heat" | "melee"

export type WeaponHudData = {
    available: boolean
    weaponName: string
    slot: number
    mode: WeaponHudMode
    currentAmmo: number
    reserveAmmo: number
    magazineSize: number
    heatRatio: number
    reloading: boolean
    lowAmmo: boolean
}

const RuntimeCS = (globalThis as any).CS
const separator = "\u001f"

const previewState: WeaponHudData = {
    available: true,
    weaponName: "KINEMATION TR15",
    slot: 1,
    mode: "ballistic",
    currentAmmo: 30,
    reserveAmmo: 30,
    magazineSize: 30,
    heatRatio: 0,
    reloading: false,
    lowAmmo: false,
}

const emptyState: WeaponHudData = {
    available: false,
    weaponName: "NO WEAPON",
    slot: 0,
    mode: "none",
    currentAmmo: 0,
    reserveAmmo: 0,
    magazineSize: 0,
    heatRatio: 0,
    reloading: false,
    lowAmmo: false,
}

function encode(state: WeaponHudData): string {
    return [
        state.available ? 1 : 0,
        state.weaponName.replace(separator, " "),
        state.slot,
        state.mode,
        state.currentAmmo,
        state.reserveAmmo,
        state.magazineSize,
        state.heatRatio,
        state.reloading ? 1 : 0,
        state.lowAmmo ? 1 : 0,
    ].join(separator)
}

export function readWeaponHudSnapshot(): string {
    if (!__isPlaying) return encode(previewState)

    try {
        return String(RuntimeCS.FirstPersonProject.UI.ProjectSapphireBridge.ReadWeaponHudSnapshot())
    } catch {
        return encode(emptyState)
    }
}

export function decodeWeaponHudSnapshot(snapshot: string | undefined): WeaponHudData {
    if (!snapshot) return { ...emptyState }

    const fields = snapshot.split(separator)
    if (fields.length !== 10) return { ...emptyState }

    return {
        available: fields[0] === "1",
        weaponName: fields[1],
        slot: Number(fields[2]),
        mode: fields[3] as WeaponHudMode,
        currentAmmo: Number(fields[4]),
        reserveAmmo: Number(fields[5]),
        magazineSize: Number(fields[6]),
        heatRatio: Number(fields[7]),
        reloading: fields[8] === "1",
        lowAmmo: fields[9] === "1",
    }
}

