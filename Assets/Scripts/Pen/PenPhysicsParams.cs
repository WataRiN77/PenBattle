using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace PenBattleGame.Core
{
    public class PenPhysicsParams
    {
        public float mass { get; set; } = 1.0f;
        public float bounciness { get; set; } = 0.02f;
        public float friction { get; set; } = 0.05f;
        public float radius { get; set; } = 0.5f;
        public float maxSpeed { get; set; } = 30.0f;
        
        public PenPhysicsParams Clone()
        {
            return new PenPhysicsParams
            {
                mass = this.mass,
                bounciness = this.bounciness,
                friction = this.friction,
                radius = this.radius,
                maxSpeed = this.maxSpeed
            };
        }
    }

    public class Pen
    {
        public string id { get; private set; }
        public Vector3 position { get; set; }
        public Vector3 velocity { get; set; }
        public float rotation { get; set; }
        public float angularVelocity { get; set; }
        public PenPhysicsParams physics { get; private set; }
        public bool isActive { get; set; } = true;

        /// <summary>
        /// Apply temporary changes on Pens.
        /// </summary>
        private float _bouncinessOverride = -1f;
        private float _speedMultiplierOverride = 1f;

        public Pen(string _id, Vector3 _position)
        {
            id = _id;
            position = _position;
            velocity = Vector3.Zero;
            rotation = 0f;
            angularVelocity = 0f;
            physics = new PenPhysicsParams();
            isActive = true;
        }

        public void ApplyBouncinessModifier(float value)
        {
            _bouncinessOverride = value;
        }
        public void ApplySpeedMultiplier(float multiplier)
        {
            _speedMultiplierOverride = multiplier;
        }
        public void ApplyModifiers(float bounciness = -1f, float speed = 1f)
        {
            ApplyBouncinessModifier(bounciness);
            ApplySpeedMultiplier(speed);
        }
        public void ClearModifiers()
        {
            ApplyModifiers(-1, 1);
        }

        public float GetEffectiveBounciness()
        {
            return _bouncinessOverride >= 0 ? _bouncinessOverride : physics.bounciness;
        }
        public float GetEffectiveSpeedMultiplier()
        {
            return _speedMultiplierOverride;
        }

        public void OnDeath()
        {
            EventBus.Instance.Publish(new PenEliminatedEvent
            {
                penId = this.id,
                killerId = null
            });
        }

        public void Reset(Vector3 spawnPosition)
        {
            position = spawnPosition;
            velocity = Vector3.Zero;
            rotation = 0f;
            angularVelocity = 0f;
            ClearModifiers();
        }

        public override string ToString()
        {
            return $"Pen[{id}] Pos({position.X:F2}, {position.Y:F2}, {position.Z:F2}) Vel({velocity.Length():F2})";
        }
    }

    /// <summary>
    /// All collision and physics update.
    /// </summary>
    public class PhysicsWorld
    {
        private readonly Dictionary<string, Pen> _pens = new();
        private readonly Dictionary<string, string> _lastCollisionMap = new();

        public void AddPen(Pen pen)
        {
            if (!_pens.ContainsKey(pen.id))
                _pens[pen.id] = pen;
        }
        public Pen GetPenFromID(string id) => _pens.TryGetValue(id, out var p) ? p : null;
        public IEnumerable<Pen> GetAllPens() => _pens.Values;
        public void RemovePenFromID(string id) => _pens.Remove(id);
        public void LaunchPen(string id, Vector3 direction, float force)
        {
            var pen = GetPenFromID(id);
            if(pen != null && pen.isActive)
            {
                pen.velocity += Vector3.Normalize(direction) * force;
            }
        }
    }
}
