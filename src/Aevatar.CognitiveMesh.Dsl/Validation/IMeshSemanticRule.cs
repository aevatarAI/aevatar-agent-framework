using Aevatar.CognitiveMesh.Dsl.Models;

namespace Aevatar.CognitiveMesh.Dsl.Validation;

public interface IMeshSemanticRule
{
    IEnumerable<DslValidationError> Validate(MeshDefinition definition);
}

