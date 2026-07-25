using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;

namespace HordeAttack
{
    /// <summary>
    /// Checks that the Unity Gaming Services the co-op phases depend on are actually reachable.
    /// </summary>
    /// <remarks>
    /// Whether a service is switched on lives only on the dashboard, so there is nothing local to
    /// inspect. The only trustworthy check is to exercise the services for real: sign in, then run
    /// a read-only session query. Both calls are free and leave nothing behind.
    ///
    /// This lives in the runtime assembly rather than the editor one because Unity refuses to
    /// initialize services outside play mode, so the check can only ever run from a play-mode test
    /// or from the running game.
    /// </remarks>
    public static class UgsPreflight
    {
        public static async Task<Report> RunAsync()
        {
            var report = new Report();

            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized)
                    await UnityServices.InitializeAsync();

                report.ProjectLinked = true;
            }
            catch (Exception e)
            {
                report.ProjectLinkedError = Describe(e);
                return report;
            }

            // Anonymous sign-in is the cheapest proof that Authentication is switched on.
            // A project with the service disabled rejects the request outright.
            try
            {
                if (!AuthenticationService.Instance.IsSignedIn)
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();

                report.Authentication = true;
                report.PlayerId = AuthenticationService.Instance.PlayerId;
            }
            catch (Exception e)
            {
                report.AuthenticationError = Describe(e);
                return report; // Sessions need a signed-in player, so there is nothing left to test.
            }

            // A query returning zero sessions is still a success: it proves the service answered.
            try
            {
                var results = await MultiplayerService.Instance.QuerySessionsAsync(new QuerySessionsOptions());

                report.Multiplayer = true;
                report.SessionsFound = results.Sessions.Count;
            }
            catch (Exception e)
            {
                report.MultiplayerError = Describe(e);
            }

            return report;
        }

        static string Describe(Exception e) => $"{e.GetType().Name}: {e.Message}";

        public class Report
        {
            public bool ProjectLinked;
            public string ProjectLinkedError;
            public bool Authentication;
            public string AuthenticationError;
            public string PlayerId;
            public bool Multiplayer;
            public string MultiplayerError;
            public int SessionsFound;

            public bool AllPassed => ProjectLinked && Authentication && Multiplayer;

            public override string ToString()
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("=== Comprobación de servicios UGS ===");
                sb.AppendLine(Line("Proyecto vinculado / UnityServices", ProjectLinked, ProjectLinkedError));
                sb.AppendLine(Line("Authentication", Authentication, AuthenticationError,
                    Authentication ? $"player id {PlayerId}" : null));
                sb.AppendLine(Line("Multiplayer / Sessions", Multiplayer, MultiplayerError,
                    Multiplayer ? $"{SessionsFound} sesiones visibles" : null));
                sb.AppendLine();
                sb.Append(AllPassed
                    ? "TODO OK. La Fase 4 (co-op en red) puede arrancar."
                    : "FALTA ALGO. Revisa el dashboard: cloud.unity.com > proyecto HordeAttack.");

                return sb.ToString();
            }

            static string Line(string name, bool ok, string error, string detail = null)
            {
                if (!ok)
                    return $"  [FALLA] {name}: {error}";

                return detail == null ? $"  [OK] {name}" : $"  [OK] {name} ({detail})";
            }
        }
    }
}
