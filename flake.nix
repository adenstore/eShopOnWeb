{
    description = "C# dev flake with dotnet environment for eShopOnWeb";

    inputs = {
        nixpkgs.url = "github:nixos/nixpkgs/nixos-unstable";
    };

    outputs = { self, nixpkgs, ... }:
    let
        system = "x86_64-linux";
        pkgs = nixpkgs.legacyPackages.${system};

        # global.json pins the SDK to 8.0.x and every project targets net8.0
        dotnet = pkgs.dotnet-sdk_8;

        # dotnet-ef must match the EntityFrameworkCore version in Directory.Packages.props
        efVersion = "8.0.2";

        # The .NET 8 SDK has no `dotnet completions` (that arrived in .NET 9), so use the
        # zsh snippet from the docs instead
        dotnetCompletion = pkgs.writeText "dotnet-completion.zsh" ''
            _dotnet_zsh_complete() {
                local completions=("$(dotnet complete "$words")")
                reply=( "''${(ps:\n:)completions}" )
            }
            compctl -K _dotnet_zsh_complete dotnet
        '';

        # docker-compose.yml starts azure-sql-edge on localhost:1433 with this sa password
        sqlConnection = database:
            "Server=localhost,1433;Initial Catalog=${database};User Id=sa;"
            + "Password=@someThingComplicated1234;TrustServerCertificate=true;";
    in
    {
        devShells.${system}.default = pkgs.mkShell {
            packages = [
                dotnet
                pkgs.sqlcmd
            ];

            shellHook = ''
                export DOTNET_ROOT=${dotnet}/share/dotnet
                export DOTNET_MSBUILD_SDK_RESOLVER_SDKS_DIR=${dotnet}/share/dotnet/sdk
                export MSBuildSDKsPath=${dotnet}/share/dotnet/sdk/${dotnet.version}/Sdks

                # Keep the package cache, CLI state and local tools inside the project
                export NUGET_PACKAGES=$PWD/.nupkg
                export DOTNET_CLI_HOME=$PWD/.dotnet
                export DOTNET_CLI_TELEMETRY_OPTOUT=1
                export PATH=$PWD/.dotnet/tools:$PATH

                # appsettings.json points at (localdb)\mssqllocaldb, which does not exist
                # outside Windows — point the app at the SQL Server from docker-compose.yml
                # instead of editing tracked config:  docker compose up -d sqlserver
                export ASPNETCORE_ENVIRONMENT=Development
                export ConnectionStrings__CatalogConnection="${sqlConnection "Microsoft.eShopOnWeb.CatalogDb"}"
                export ConnectionStrings__IdentityConnection="${sqlConnection "Microsoft.eShopOnWeb.Identity"}"

                if [ ! -d "$NUGET_PACKAGES" ]; then
                    echo "Restoring NuGet packages for project..."
                    dotnet restore eShopOnWeb.sln
                fi

                if [ -f .config/dotnet-tools.json ]; then
                    dotnet tool restore
                elif [ ! -x .dotnet/tools/dotnet-ef ]; then
                    echo "Installing dotnet-ef ${efVersion} into ./.dotnet/tools ..."
                    dotnet tool install dotnet-ef --version ${efVersion} --tool-path "$PWD/.dotnet/tools"
                fi

                export SHELL=$(which zsh)
                exec zsh -l -c 'source ${dotnetCompletion}; exec zsh'
            '';
        };
    };
}
