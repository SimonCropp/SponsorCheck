// Duplicated from VerifyTests/DiffEngine/src/DiffEngine/BuildServerDetector.cs.
// Used by the bundler and the live tests to flip token-missing / skip messages between user-secrets
// (recommended locally) and env-var (recommended on CI). If the upstream copy changes meaningfully,
// re-sync this file rather than editing in place.
// ReSharper disable CommentTypo

public static class BuildServerDetector
{
    static BuildServerDetector()
    {
        var variables = Environment.GetEnvironmentVariables();
        // Jenkins
        // https://wiki.jenkins.io/display/JENKINS/Building+a+software+project#Buildingasoftwareproject-belowJenkinsSetEnvironmentVariables
        var isJenkins = variables.Contains("JENKINS_URL");

        // GitHub Action
        // https://help.github.com/en/actions/automating-your-workflow-with-github-actions/using-environment-variables#default-environment-variables
        var isGithubAction = variables.Contains("GITHUB_ACTION");

        // TeamCity
        // https://www.jetbrains.com/help/teamcity/predefined-build-parameters.html#PredefinedBuildParameters-ServerBuildProperties
        var isTeamCity = variables.Contains("TEAMCITY_VERSION");

        // MyGet
        // https://docs.myget.org/docs/reference/build-services#Available_Environment_Variables
        var isMyGet = ValueEquals(variables, "BuildRunner", "MyGet");

        // GitLab
        // https://docs.gitlab.com/ee/ci/variables/predefined_variables.html
        var isGitLab = variables.Contains("GITLAB_CI");

        // GoDC
        // https://docs.gocd.org/current/faq/dev_use_current_revision_in_build.html
        var isGoDc = variables.Contains("GO_SERVER_URL");

        // Travis
        // https://docs.travis-ci.com/user/environment-variables/#default-environment-variables
        var isTravis = variables.Contains("TRAVIS_BUILD_ID");

        // Docker
        // https://www.hanselman.com/blog/detecting-that-a-net-core-app-is-running-in-a-docker-container-and-skippablefacts-in-xunit
        var isDocker = ValueEquals(variables, "DOTNET_RUNNING_IN_CONTAINER", "true");

        // AppVeyor
        // https://www.appveyor.com/docs/environment-variables/
        var isAppVeyor = variables.Contains("APPVEYOR");

        var isWsl = variables.Contains("WSL_DISTRO_NAME");

        // AzureDevops
        // https://docs.microsoft.com/en-us/azure/devops/pipelines/build/variables?view=azure-devops&tabs=yaml#agent-variables
        // Variable name is 'Agent.Id' to detect if this is a Azure Pipelines agent.
        // Note that variables are upper-cased and '.' is replaced with '_' on Azure Pipelines.
        // https://docs.microsoft.com/en-us/azure/devops/pipelines/process/variables?view=azure-devops&tabs=yaml%2Cbatch#access-variables-through-the-environment
        var isAzureDevops = ValueEquals(variables, "TF_BUILD", "True");

        Detected = isTravis ||
                   isJenkins ||
                   isGithubAction ||
                   isAzureDevops ||
                   isTeamCity ||
                   isGitLab ||
                   isMyGet ||
                   isGoDc ||
                   isDocker ||
                   isWsl ||
                   isAppVeyor;
    }

    static bool ValueEquals(IDictionary variables, string key, string value)
    {
        var variable = variables[key];
        if(variable == null)
        {
            return false;
        }

        return string.Equals((string)variable, value, StringComparison.OrdinalIgnoreCase);
    }

    public static bool Detected { get; }
}
