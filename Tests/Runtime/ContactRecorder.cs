using System.Collections.Generic;
using UnityEngine;

namespace PuzzleBox.Tests
{
    /// <summary>
    /// Records the OnContactEnter/Stay/Exit messages a KinematicMotion2D sends to its own GameObject,
    /// with the step each one arrived in.
    /// </summary>
    public class ContactRecorder : MonoBehaviour
    {
        public enum Kind
        {
            Enter,
            Stay,
            Exit,
        }

        public struct Record
        {
            public Kind kind;
            public int step;
            public KinematicMotion2D.Contact contact;
        }

        public readonly List<Record> records = new List<Record>();

        SteppedKinematicMotion2D motion;

        void Awake()
        {
            motion = GetComponent<SteppedKinematicMotion2D>();
        }

        // Contacts are reported from inside the body's FixedUpdate, before it counts the step as taken.
        int CurrentStep => motion != null ? motion.stepsTaken + 1 : 0;

        void OnContactEnter(KinematicMotion2D.Contact contact) => Add(Kind.Enter, contact);

        void OnContactStay(KinematicMotion2D.Contact contact) => Add(Kind.Stay, contact);

        void OnContactExit(KinematicMotion2D.Contact contact) => Add(Kind.Exit, contact);

        void Add(Kind kind, KinematicMotion2D.Contact contact)
        {
            records.Add(new Record { kind = kind, step = CurrentStep, contact = contact });
        }

        /// <summary>The records of <paramref name="kind"/> about <paramref name="collider"/>, optionally only those of one step.</summary>
        public List<Record> Find(Kind kind, Collider2D collider, int step = -1)
        {
            return records.FindAll(r => r.kind == kind && r.contact.collider == collider && (step < 0 || r.step == step));
        }

        public int Count(Kind kind, Collider2D collider, int step = -1)
        {
            return Find(kind, collider, step).Count;
        }
    }
}
