using ArchonAI.Core.Models.Onboarding;
namespace ArchonAI.Core.Interfaces;

public interface IOnboardingService
{
    Task<OnboardingDeployResult> DeployAsync(OnboardingDeployRequest request, CancellationToken ct = default);
    Task<OnboardingDeployResult> DeployTemplateAsync(OnboardingTemplateDeployRequest request, CancellationToken ct = default);
}
