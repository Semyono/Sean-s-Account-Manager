using System.IO;
using System.Text.Json;
using FemBoy_Account_Manager.Models;

namespace FemBoy_Account_Manager.Services;

public class AccountStore
{
    private readonly string _dataDir;
    private readonly string _dataFile;

    public List<Account> Accounts { get; set; } = new();
    public AccountStore()
    {
        _dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FemBoy_Account_Manager");
        _dataFile = Path.Combine(_dataDir, "accounts.json");
        Directory.CreateDirectory(_dataDir);
        Load();
    }

    public void Load()
    {
        if (!File.Exists(_dataFile))
        {
            Accounts = new List<Account>();
            return;
        }

        try
        {
            string json = File.ReadAllText(_dataFile);
            Accounts = JsonSerializer.Deserialize<List<Account>>(json) ?? new List<Account>();
            Accounts = Accounts.OrderBy(a => a.Order).ToList();
        }
        catch
        {
            Accounts = new List<Account>();
        }
    }

    public void Save()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(Accounts, options);
        File.WriteAllText(_dataFile, json);
    }

    public void AddOrUpdate(Account account)
    {
        var existing = Accounts.FirstOrDefault(a => a.UserId == account.UserId);
        if (existing != null)
        {
            Accounts.Remove(existing);
        }
        Accounts.Add(account);
        Save();
    }

    public void Remove(long userId)
    {
        Accounts.RemoveAll(a => a.UserId == userId);
        Save();
    }

    public void SaveOrder(IEnumerable<long> orderedUserIds)
    {
        int index = 0;
        var ids = orderedUserIds.ToList();
        foreach (var userId in ids)
        {
            var acc = Accounts.FirstOrDefault(a => a.UserId == userId);
            if (acc != null) acc.Order = index++;
        }
        Accounts = Accounts.OrderBy(a => a.Order).ToList();
        Save();
    }
}
