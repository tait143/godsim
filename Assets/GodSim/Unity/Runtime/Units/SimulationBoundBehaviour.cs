using System;
using GodSim.Core.Composition;
using UnityEngine;

namespace GodSim.Unity.Units
{
    /// <summary>
    /// Base for visual components that follow the runner's current <see cref="GodSimulation"/>.
    /// Hooks <see cref="GodSimRunner.SimulationCreated"/> and also polls <see cref="GodSimRunner.Simulation"/>
    /// every frame (a reference compare), so a component enabled before/after the runner, a loaded save or a
    /// new world all end up bound to the live simulation. Subclasses subscribe to the sim's EventBus in
    /// <see cref="OnSimulationBound"/> and must unsubscribe in <see cref="OnSimulationUnbound"/>.
    /// </summary>
    public abstract class SimulationBoundBehaviour : MonoBehaviour
    {
        private GodSimRunner _runner;
        private Action<GodSimulation> _onCreated;

        /// <summary>The simulation currently bound (null when none).</summary>
        protected GodSimulation Sim { get; private set; }

        /// <summary>The runner hooked (may be null).</summary>
        protected GodSimRunner Runner => _runner;

        protected virtual void OnEnable()
        {
            if (_onCreated == null) _onCreated = HandleSimulationCreated;
            HookRunner();
        }

        protected virtual void OnDisable()
        {
            UnhookRunner();
            BindTo(null);
        }

        /// <summary>Call first thing in Update/LateUpdate. Returns true when a simulation with a map is bound.</summary>
        protected bool EnsureBound()
        {
            if (_runner == null)
            {
                HookRunner();
                if (_runner == null)
                {
                    if (Sim != null) BindTo(null);
                    return false;
                }
            }
            GodSimulation current = _runner.Simulation;
            if (!ReferenceEquals(current, Sim)) BindTo(current);
            return Sim != null && Sim.State != null && Sim.State.Map != null;
        }

        private void HookRunner()
        {
            GodSimRunner r = GodSimRunner.Instance;
            if (r == null || ReferenceEquals(r, _runner)) return;
            UnhookRunner();
            _runner = r;
            _runner.SimulationCreated += _onCreated;
        }

        private void UnhookRunner()
        {
            // ReferenceEquals: still unsubscribe from a runner Unity has already destroyed.
            if (!ReferenceEquals(_runner, null) && _onCreated != null) _runner.SimulationCreated -= _onCreated;
            _runner = null;
        }

        private void HandleSimulationCreated(GodSimulation sim)
        {
            BindTo(sim);
        }

        private void BindTo(GodSimulation sim)
        {
            if (ReferenceEquals(sim, Sim)) return;
            GodSimulation old = Sim;
            Sim = null;
            if (old != null)
            {
                try { OnSimulationUnbound(old); }
                catch (Exception ex) { Debug.LogException(ex, this); }
            }
            if (sim == null || sim.State == null || sim.State.Map == null) return;
            Sim = sim;
            try { OnSimulationBound(sim); }
            catch (Exception ex) { Debug.LogException(ex, this); }
        }

        /// <summary>A new simulation became current: subscribe to its events and rebuild caches.</summary>
        protected abstract void OnSimulationBound(GodSimulation sim);

        /// <summary>The previous simulation went away: unsubscribe from its events and release visuals.</summary>
        protected abstract void OnSimulationUnbound(GodSimulation old);
    }
}
