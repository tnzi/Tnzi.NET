namespace Tnzi.Domain.Entities;

public interface IHasCreationTime
{
    DateTime CreationTime { get; set; }
}

public interface IHasModificationTime
{
    DateTime? LastModificationTime { get; set; }
}

