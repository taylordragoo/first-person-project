namespace FPSProject.Multiplayer.Core.Movement
{
    /// <summary>
    /// Controls which CAS update path a <see cref="Unity.Netcode.NetworkBehaviour"/> player runs.
    /// Only one mode is active at a time per player instance.
    /// </summary>
    public enum PlayerSimulationMode
    {
        /// <summary>
        /// The owning client runs the existing CAS Update() path unchanged: input, grounding,
        /// movement, look, camera, and CharacterController.Move. Motion samples are submitted
        /// to the host for validation.
        /// </summary>
        LocalOwner,

        /// <summary>
        /// Remote proxies skip CAS grounding, physics, movement, look input, and camera control;
        /// accepted presentation state is applied to the hidden CAS source rig so the
        /// CasTacticalPlayerBridge and Tactical presentation remain visually correct.
        /// </summary>
        RemoteProxy,

        /// <summary>
        /// No input, motor, or proxy animation work. Used during death and despawn.
        /// </summary>
        Disabled
    }
}