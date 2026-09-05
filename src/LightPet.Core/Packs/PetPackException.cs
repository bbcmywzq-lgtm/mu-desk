namespace LightPet.Core.Packs;

public sealed class PetPackException : Exception
{
    public PetPackException(string message)
        : base(message)
    {
    }

    public PetPackException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
