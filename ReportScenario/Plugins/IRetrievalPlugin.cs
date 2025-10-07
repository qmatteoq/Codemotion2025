using System.ComponentModel;

public interface IRetrievalPlugin
{
    void Authenticate();
    Task<List<string>> GetExtractsAsync([Description("The prompt of the user")] string prompt);

    bool IsClientInitialized { get; }
}