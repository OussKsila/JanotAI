using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.SemanticKernel;

namespace JanotAi.Plugins;

/// <summary>
/// Plugin SK natif pour envoyer des messages via WhatsApp Desktop (Windows).
/// Supporte les noms de contacts via contacts.json + numéros directs.
/// </summary>
public class WhatsAppPlugin
{
    // ─── Win32 API ────────────────────────────────────────────────────────────

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const byte VK_RETURN       = 0x0D;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const int  SW_RESTORE      = 9;

    private static readonly string ContactsFile =
        Path.Combine(AppContext.BaseDirectory, "contacts.json");

    // ─── Outils ──────────────────────────────────────────────────────────────

    [KernelFunction("send_whatsapp_message")]
    [Description("Envoie un message WhatsApp. Accepte un numéro international (+12025551234) OU un nom de contact sauvegardé (ex: john_doe).")]
    public async Task<string> SendWhatsAppMessageAsync(
        [Description("Numéro de téléphone international (ex: +12025551234) ou nom du contact (ex: john_doe)")]
        string phoneOrName,
        [Description("Le message à envoyer")]
        string message)
    {
        var phone = ResolveContact(phoneOrName);
        if (phone is null)
            return $"Contact '{phoneOrName}' not found in contacts.json. Provide the phone number in international format (e.g. +12025551234).";

        var cleanPhone = phone.Replace("+", "").Replace(" ", "").Replace("-", "").Trim();

        var uri = $"whatsapp://send?phone={cleanPhone}&text={Uri.EscapeDataString(message)}";
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            return $"Impossible d'ouvrir WhatsApp Desktop. Vérifie qu'il est installé. Erreur : {ex.Message}";
        }

        await Task.Delay(4000);

        var hwnd = GetWhatsAppWindow();
        if (hwnd == IntPtr.Zero)
            return $"WhatsApp ouvert avec le message pré-rempli pour +{cleanPhone}. Appuie manuellement sur Entrée.";

        ShowWindow(hwnd, SW_RESTORE);
        SetForegroundWindow(hwnd);
        await Task.Delay(600);

        keybd_event(VK_RETURN, 0, 0,               UIntPtr.Zero);
        await Task.Delay(100);
        keybd_event(VK_RETURN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

        return $"✓ Message envoyé à {phoneOrName} (+{cleanPhone}) via WhatsApp.";
    }

    [KernelFunction("save_whatsapp_contact")]
    [Description("Enregistre un contact WhatsApp (nom + numéro) pour pouvoir lui écrire par son nom ensuite")]
    public Task<string> SaveContactAsync(
        [Description("Nom ou surnom du contact (ex: john_doe, maman, boss)")]
        string name,
        [Description("Numéro de téléphone international (ex: +12025551234)")]
        string phone)
    {
        var contacts = LoadContacts();
        var existing = contacts.FirstOrDefault(c =>
            c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
            existing.Phone = phone.Trim();
        else
            contacts.Add(new Contact { Name = name.Trim().ToLower(), Phone = phone.Trim() });

        SaveContacts(contacts);
        return Task.FromResult($"✓ Contact '{name}' → {phone} enregistré.");
    }

    [KernelFunction("list_whatsapp_contacts")]
    [Description("Liste tous les contacts WhatsApp enregistrés")]
    public Task<string> ListContactsAsync()
    {
        var contacts = LoadContacts();
        if (contacts.Count == 0)
            return Task.FromResult("Aucun contact enregistré. Utilise save_whatsapp_contact pour en ajouter.");

        var lines = contacts.Select(c => $"  • {c.Name} → {c.Phone}");
        return Task.FromResult("Contacts WhatsApp :\n" + string.Join("\n", lines));
    }

    [KernelFunction("open_whatsapp_chat")]
    [Description("Ouvre WhatsApp Desktop sur le chat d'un contact (nom ou numéro) sans envoyer de message")]
    public async Task<string> OpenWhatsAppChatAsync(
        [Description("Numéro international ou nom du contact")]
        string phoneOrName)
    {
        var phone = ResolveContact(phoneOrName);
        if (phone is null)
            return $"Contact '{phoneOrName}' introuvable.";

        var cleanPhone = phone.Replace("+", "").Replace(" ", "").Replace("-", "").Trim();
        var uri = $"whatsapp://send?phone={cleanPhone}";

        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            await Task.Delay(2000);
            var hwnd = GetWhatsAppWindow();
            if (hwnd != IntPtr.Zero) { ShowWindow(hwnd, SW_RESTORE); SetForegroundWindow(hwnd); }
            return $"Chat WhatsApp ouvert pour {phoneOrName} (+{cleanPhone}).";
        }
        catch (Exception ex)
        {
            return $"Erreur : {ex.Message}";
        }
    }

    [KernelFunction("get_whatsapp_status")]
    [Description("Vérifie si WhatsApp Desktop est en cours d'exécution")]
    public Task<string> GetWhatsAppStatusAsync()
    {
        var procs = Process.GetProcessesByName("WhatsApp");
        return Task.FromResult(procs.Length == 0
            ? "WhatsApp Desktop n'est pas en cours d'exécution."
            : $"WhatsApp Desktop est actif. Prêt à envoyer des messages.");
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static IntPtr GetWhatsAppWindow()
    {
        foreach (var proc in Process.GetProcessesByName("WhatsApp"))
            if (proc.MainWindowHandle != IntPtr.Zero) return proc.MainWindowHandle;
        return IntPtr.Zero;
    }

    /// <summary>Résout un nom de contact ou retourne le numéro tel quel s'il ressemble à un numéro.</summary>
    private static string? ResolveContact(string phoneOrName)
    {
        var input = phoneOrName.Trim();

        // C'est déjà un numéro (commence par + ou chiffres)
        if (input.StartsWith("+") || input.All(c => char.IsDigit(c) || c == ' ' || c == '-'))
            return input;

        // Chercher dans contacts.json
        var contacts = LoadContacts();
        var match = contacts.FirstOrDefault(c =>
            c.Name.Equals(input, StringComparison.OrdinalIgnoreCase));

        return match?.Phone;
    }

    private static List<Contact> LoadContacts()
    {
        if (!File.Exists(ContactsFile)) return [];
        try
        {
            var json = File.ReadAllText(ContactsFile);
            var doc  = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("contacts")
                .EnumerateArray()
                .Select(e => new Contact
                {
                    Name  = e.GetProperty("name").GetString()  ?? "",
                    Phone = e.GetProperty("phone").GetString() ?? ""
                })
                .ToList();
        }
        catch { return []; }
    }

    private static void SaveContacts(List<Contact> contacts)
    {
        var json = JsonSerializer.Serialize(
            new { contacts },
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ContactsFile, json);
    }

    private class Contact
    {
        public string Name  { get; set; } = "";
        public string Phone { get; set; } = "";
    }
}
