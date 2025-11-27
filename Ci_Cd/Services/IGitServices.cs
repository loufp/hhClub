namespace Ci_Cd.Services;

public interface IGitServices
{
    string CloneRepository(string rep);//clone and return psht to repo

    void DeleteRepository(string path);//delete
}