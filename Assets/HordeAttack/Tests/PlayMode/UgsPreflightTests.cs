using System.Collections;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HordeAttack.Tests
{
    /// <summary>
    /// Preflight gate for the networked phases: proves the Unity Gaming Services this game
    /// depends on are switched on for the linked project.
    /// </summary>
    /// <remarks>
    /// These are integration tests on purpose. Whether Authentication or Multiplayer/Sessions is
    /// enabled exists only on the dashboard, so a check that does not hit the real services proves
    /// nothing. They need the machine to be online and the project to be linked, which is exactly
    /// the condition being asserted.
    ///
    /// Play mode, not edit mode: Unity throws
    /// <c>ServicesInitializationException</c> if services are initialized outside play mode.
    /// </remarks>
    [Category("UgsPreflight")]
    public class UgsPreflightTests
    {
        const float k_TimeoutSeconds = 60f;

        static UgsPreflight.Report s_Report;

        /// <summary>
        /// Runs the preflight once and caches it, so the three assertions below report on the same
        /// run instead of signing in three times.
        /// </summary>
        static IEnumerator EnsureReport()
        {
            if (s_Report != null)
                yield break;

            Task<UgsPreflight.Report> task = UgsPreflight.RunAsync();

            float deadline = Time.realtimeSinceStartup + k_TimeoutSeconds;
            while (!task.IsCompleted)
            {
                if (Time.realtimeSinceStartup > deadline)
                    Assert.Fail($"UGS did not answer within {k_TimeoutSeconds}s. Check the network connection.");

                yield return null;
            }

            Assert.That(task.IsFaulted, Is.False, $"Preflight threw: {task.Exception}");

            s_Report = task.Result;
            Debug.Log(s_Report.ToString());
        }

        [UnityTest]
        public IEnumerator ProjectIsLinkedAndServicesInitialize()
        {
            yield return EnsureReport();

            Assert.That(s_Report.ProjectLinked, Is.True,
                "UnityServices failed to initialize. The project is probably not linked to a Unity " +
                $"Cloud project (Edit > Project Settings > Services). Error: {s_Report.ProjectLinkedError}");
        }

        [UnityTest]
        public IEnumerator AuthenticationIsEnabled()
        {
            yield return EnsureReport();

            Assert.That(s_Report.Authentication, Is.True,
                "Anonymous sign-in failed, so Authentication is not enabled on the dashboard. " +
                $"Error: {s_Report.AuthenticationError}");
            Assert.That(s_Report.PlayerId, Is.Not.Null.And.Not.Empty,
                "Sign-in reported success but handed back no player id.");
        }

        [UnityTest]
        public IEnumerator MultiplayerSessionsIsEnabled()
        {
            yield return EnsureReport();

            Assert.That(s_Report.Multiplayer, Is.True,
                "The session query was rejected, so Multiplayer/Sessions is not enabled on the " +
                $"dashboard. Error: {s_Report.MultiplayerError}");
        }
    }
}
