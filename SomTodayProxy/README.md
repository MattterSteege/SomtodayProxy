# SomTodayProxy

SomTodayProxy is an ASP.NET Core proxy application designed to facilitate authentication with SomToday by forwarding requests to SomToday's API and redirecting responses back to the client. This application enables the interception and management of responses, such as authentication tokens, from SomToday.

### Acknowledgements

Special thanks to [Micha](https://micha.ga) ([GitHub: FurriousFox](https://github.com/FurriousFox)) for discovering the original method of authenticating users with SomToday. This project wouldn't have been possible without their discovery and guidance.

## Table of Contents

- [Getting Started](#getting-started)
    - [Prerequisites](#prerequisites)
    - [Installation](#installation)
    - [Running the Application](#running-the-application)
- [Usage](#usage)
    - [How It Works](#how-it-works)
    - [Endpoints](#endpoints)
    - [Proxy Mechanism](#proxy-mechanism)
- [Configuration](#configuration)
    - [Environment Settings](#environment-settings)
    - [HTTPS and HTTP](#https-and-http)
- [Code Overview](#code-overview)
    - [Startup Class](#startup-class)
    - [Program Class](#program-class)
    - [Request Handling](#request-handling)
- [Contributing](#contributing)
- [License](#license)

## Getting Started

### Prerequisites

- [.NET 6.0 SDK or later](https://dotnet.microsoft.com/download)
- A working knowledge of ASP.NET Core
- Basic understanding of proxy servers

### Installation

1. **Clone the repository:**
   ```sh
   git clone https://github.com/yourusername/SomTodayProxy.git
   cd SomTodayProxy
   ```

2. **Install the dependencies:**
   Restore the .NET packages by running:
   ```sh
   dotnet restore
   ```

### Running the Application

1. **Build the application:**
   ```sh
   dotnet build
   ```

2. **Run the application:**
   You can run the application in either `Development` or `Production` mode:

   ```sh
   dotnet run
   ```

   This will start the server at the specified URLs (default: `https://localhost:5001` and `http://localhost:5000`).

* You do need to make sure that you have a vps (or cloudflare tunnel etc.) that points a domain to the server. So for example create a cloudflare tunnel that points your domain to `localhost:5001` and `localhost:5000` and then you can use the domain to access the proxy.

## Usage

### How It Works

SomTodayProxy functions as a middleman between the client and SomToday's API. It forwards HTTP requests from the client to SomToday and then returns the response from SomToday back to the client. This process allows the application to manage authentication tokens and other sensitive data, ensuring that the authentication process is handled securely.

### Endpoints

- **`GET /`**: Returns a simple message indicating that the proxy is running.

- **`GET /.well-known/openid-configuration`**: Forwards the OpenID configuration request to SomToday's API and returns the response.

- **Fallback (`/*`)**: Forwards all other requests to the appropriate SomToday endpoint based on the request path.

### Proxy Mechanism

1. **Request Forwarding**: Incoming requests are forwarded to the appropriate SomToday API endpoint.

2. **Response Handling**: The proxy handles responses, including setting appropriate CORS headers and managing specific paths like `/oauth2/token` and `/oauth2/authorize`.

3. **Custom Token Handling**: The proxy intercepts and logs OAuth2 token requests, returning a custom response indicating successful authentication.

## Configuration

### Environment Settings

The application uses environment-based configuration to set up the development environment. The `Configure` method in the `Startup` class enables the developer exception page during development.

### HTTPS and HTTP

The application is configured to run on both HTTPS (port 5001) and HTTP (port 5000) with specific URLs defined in the `Program` class. The `UseHttpsRedirection` service ensures that all HTTP requests are redirected to HTTPS.

## Code Overview

### Startup Class

The `Startup` class is responsible for setting up the application services and middleware. Key components include:

- **`ConfigureServices`**: Adds HTTP Client services and sets up HTTPS redirection.
- **`Configure`**: Configures middleware for handling forwarded headers, routing, and endpoints.

### Program Class

The `Program` class contains the `Main` method, which is the entry point of the application. It configures the web host and sets the URLs on which the server will listen.

### Request Handling

Key methods in the `Startup` class handle the core proxy functionality:

- **`HandleOpenIdConfiguration`**: Forwards the OpenID configuration request.
- **`HandleProxyRequest`**: Handles all other proxy requests, including special handling for token and authorization endpoints.
- **`CreateProxyRequest` and `CopyHeaders`**: Facilitate the creation and modification of proxy requests.
- **`SendProxyRequestAndHandleResponse`**: Sends the proxy request and processes the response, setting CORS headers and managing the response body.

## Contributing

Contributions are welcome! If you have suggestions or improvements, feel free to submit a pull request or open an issue. Make sure to follow the project's coding standards and test thoroughly.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

TL;DR: You can do whatever you want with this code, but you must include the original license and copyright notice.