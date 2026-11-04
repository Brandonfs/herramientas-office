namespace HerramientasOffice.Services;

/// <summary>Actualizacion de Office tal como la reporta Windows Update.</summary>
public sealed record WuaItem(
    string UpdateId,
    string Kb,
    string Title,
    string Product,
    DateTime? Date,
    bool Installed,
    bool Hidden,
    IReadOnlyList<string> Supersedes);

/// <summary>
/// Acceso al Windows Update Agent (COM) apuntando al catalogo "Microsoft Update",
/// donde viven las actualizaciones de Office.
/// </summary>
public static class WindowsUpdateService
{
    private const string MicrosoftUpdateServiceId = "7971f918-a847-4430-9279-4a52d1efe18d";

    private static readonly object RegistrationLock = new();
    private static bool _registrationDone;

    private static void EnsureServiceRegistered()
    {
        // Las busquedas corren en paralelo: el registro se hace una sola vez.
        lock (RegistrationLock)
        {
            if (_registrationDone) return;
            RegisterService();
            _registrationDone = true;
        }
    }

    private static void RegisterService()
    {
        // El servicio debe estar registrado en el cliente para poder buscarlo por ID.
        // Flag 2 = solo registro online; NO se registra con Automatic Updates (flag 4),
        // para no activar "Recibir actualizaciones de otros productos de Microsoft".
        try
        {
            dynamic manager = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.ServiceManager")!)!;
            dynamic services = manager.Services;
            int count = (int)services.Count;
            for (int i = 0; i < count; i++)
            {
                if (string.Equals((string)services.Item(i).ServiceID, MicrosoftUpdateServiceId, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            manager.AddService2(MicrosoftUpdateServiceId, 2, "");
        }
        catch
        {
            // Si falla el registro, la busqueda siguiente lo reportara con un error claro.
        }
    }

    private static dynamic CreateSearcher()
    {
        dynamic session = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.Session")!)!;
        dynamic searcher = session.CreateUpdateSearcher();
        searcher.ServerSelection = 3; // ssOthers
        searcher.ServiceID = MicrosoftUpdateServiceId;
        return searcher;
    }

    /// <summary>Actualizaciones de Office disponibles y bloqueadas.</summary>
    public static List<WuaItem> SearchPending()
    {
        EnsureServiceRegistered();

        var pending = Task.Run(() => Query(installed: false, hidden: false));
        var blocked = Task.Run(() => Query(installed: false, hidden: true));

        Task.WaitAll(pending, blocked);
        return pending.Result.Concat(blocked.Result).ToList();
    }

    /// <summary>Actualizaciones de Office instaladas (solo aportan fechas y relaciones de reemplazo).</summary>
    public static List<WuaItem> SearchInstalled()
    {
        EnsureServiceRegistered();
        try { return Query(installed: true, hidden: false); }
        catch { return new List<WuaItem>(); }
    }

    /// <summary>Fecha de la ultima instalacion exitosa de cada KB segun el historial de Windows Update.</summary>
    public static Dictionary<string, DateTime> GetInstallDates()
    {
        var dates = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        try
        {
            dynamic session = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.Session")!)!;
            dynamic searcher = session.CreateUpdateSearcher();
            int total = (int)searcher.GetTotalHistoryCount();
            if (total == 0) return dates;

            dynamic history = searcher.QueryHistory(0, Math.Min(total, 2000));
            int count = (int)history.Count;
            for (int i = 0; i < count; i++)
            {
                dynamic h = history.Item(i);
                if ((int)h.Operation != 1 || (int)h.ResultCode != 2) continue; // instalacion exitosa

                string kb = OfficeProducts.ExtractKb((string)h.Title);
                if (kb.Length == 0) continue;

                var date = (DateTime)h.Date;
                if (!dates.TryGetValue(kb, out var existing) || date > existing)
                    dates[kb] = date;
            }
        }
        catch
        {
            // El historial es opcional: solo aporta la fecha de instalacion.
        }
        return dates;
    }

    private static List<WuaItem> Query(bool installed, bool hidden)
    {
        var searcher = CreateSearcher();
        string criteria = installed
            ? "IsInstalled=1 and Type='Software'"
            : $"IsInstalled=0 and IsHidden={(hidden ? 1 : 0)} and Type='Software'";

        dynamic result = searcher.Search(criteria);
        dynamic updates = result.Updates;
        int count = (int)updates.Count;
        var list = new List<WuaItem>();

        for (int i = 0; i < count; i++)
        {
            dynamic u = updates.Item(i);
            string title = (string)u.Title;

            string? product = OfficeProducts.Detect(title);
            if (product is null && HasOfficeCategory(u))
                product = "Office";
            if (product is null) continue;

            string kb = OfficeProducts.ExtractKb(title);
            if (kb.Length == 0)
            {
                try
                {
                    dynamic ids = u.KBArticleIDs;
                    if ((int)ids.Count > 0)
                        kb = "KB" + (string)ids.Item(0);
                }
                catch { }
            }

            DateTime? date = null;
            try { date = (DateTime)u.LastDeploymentChangeTime; } catch { }

            var supersedes = new List<string>();
            try
            {
                dynamic sup = u.SupersededUpdateIDs;
                int n = (int)sup.Count;
                for (int j = 0; j < n; j++)
                    supersedes.Add((string)sup.Item(j));
            }
            catch { }

            list.Add(new WuaItem(
                UpdateId: (string)u.Identity.UpdateID,
                Kb: kb,
                Title: title,
                Product: product,
                Date: date,
                Installed: installed,
                Hidden: hidden,
                Supersedes: supersedes));
        }

        return list;
    }

    private static bool HasOfficeCategory(dynamic update)
    {
        try
        {
            dynamic categories = update.Categories;
            int count = (int)categories.Count;
            for (int i = 0; i < count; i++)
            {
                string name = (string)categories.Item(i).Name;
                if (name.Contains("Office", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>Oculta o muestra las actualizaciones indicadas. Devuelve los IDs que se modificaron.</summary>
    public static HashSet<string> SetHidden(IReadOnlyCollection<string> updateIds, bool hidden)
    {
        EnsureServiceRegistered();
        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var searcher = CreateSearcher();

        dynamic result = searcher.Search($"IsInstalled=0 and IsHidden={(hidden ? 0 : 1)} and Type='Software'");
        dynamic updates = result.Updates;
        int count = (int)updates.Count;

        for (int i = 0; i < count; i++)
        {
            dynamic u = updates.Item(i);
            string id = (string)u.Identity.UpdateID;
            if (!updateIds.Contains(id, StringComparer.OrdinalIgnoreCase)) continue;

            u.IsHidden = hidden;
            changed.Add(id);
        }

        return changed;
    }
}
