## Overview of the SomToday App Proxy Or (STAP)

### Summary
The **SomtodayProxy** is a proxy server designed to mediate authentication requests between clients and the Somtoday API. It allows users to authenticate through Somtoday's official service while capturing and handling responses, particularly tokens, which can then be used by the client application. The proxy effectively hides the complexities of interacting with Somtoday’s API, allowing clients to authenticate users without dealing directly with the API.

### Key Components
- **`Startup` Class**: This is the main configuration class for the application. It sets up services, middleware, and endpoint routing for handling various types of HTTP requests.
- **`Program` Class**: This class is the entry point of the application. It configures and starts the web server.
- **Data Models**: The application uses two primary data models:
    - **`SomtodayAuthenticatieModel`**: Represents the data structure required for Somtoday's OAuth2 token requests.
    - **`UserAuthenticatingModel`**: Represents users attempting to authenticate, including their `vanityUrl`, expiration time, and callback URL.

### Endpoints Overview

The proxy server exposes several key endpoints to interact with clients and the Somtoday API:

1. **`GET /`**:
    - **Description**: The root endpoint of the server. It returns a simple message indicating that the proxy is running.
    - **Response**: A plain text message detailing the purpose of the proxy and crediting the contributor who helped discover the authentication method.

2. **`GET /.well-known/openid-configuration`**:
    - **Description**: This endpoint forwards a request to the Somtoday OpenID configuration URL. It fetches the OpenID Connect configuration from Somtoday's servers and returns it to the client.
    - **Response**: JSON content containing OpenID Connect metadata for Somtoday.

3. **`GET /requestUrl`**:
    - **Description**: This endpoint initiates a new authentication request for a user. The client provides a `user` identifier and a `callbackUrl` to which the token should be sent after authentication.
    - **Parameters**:
        - `user`: The identifier of the user requesting authentication.
        - `callbackUrl`: The URL to which the authentication token will be sent upon successful login.
    - **Response**: A JSON object containing the `vanityUrl` (a unique URL identifier) and other relevant information for the authentication session.

4. **Fallback Endpoint** (`MapFallback`):
    - **Description**: This is a catch-all endpoint for any requests that don't match the predefined routes. It acts as a proxy for all other requests, forwarding them to Somtoday’s API while handling specific OAuth2 flows and token retrieval.
    - **Behavior**:
        - **OAuth2 Authorization (`/oauth2/authorize`)**: Redirects the request to Somtoday’s authorization endpoint.
        - **OAuth2 Token (`/oauth2/token`)**: Handles token requests by capturing and processing the token, then forwarding it to the client's callback URL.

### Detailed Endpoint Operations

- **Handling OpenID Configuration**:
    - The `HandleOpenIdConfiguration` method sends a GET request to the Somtoday OpenID Connect configuration endpoint, retrieves the configuration JSON, and returns it directly to the client. This allows the proxy to seamlessly integrate with clients expecting standard OpenID Connect metadata.

- **Initiating Authentication Requests**:
    - The `RequestLoginUrl` method processes requests for initiating user authentication. It generates a unique `vanityUrl` for each user, which is later used to track and manage authentication sessions. If the necessary parameters (`user` and `callbackUrl`) are not provided, the server responds with a `400 Bad Request` status, indicating missing parameters.

- **Proxying Requests and Handling Responses**:
    - The `HandleProxyRequest` method is the heart of the proxy functionality. It forwards requests to either Somtoday's API or login endpoint, depending on the request path. For OAuth2 token requests, it captures the token and forwards it to the client’s specified callback URL.
    - The `SendProxyRequestAndHandleResponse` method sends the proxied request and processes the response, ensuring that headers, including CORS headers, are correctly handled to maintain security and compatibility with modern web clients.

### Conclusion

The **SomtodayProxy** is a specialized tool designed to facilitate user authentication with Somtoday by acting as an intermediary. It manages user sessions, proxies requests, and handles OAuth2 tokens in a streamlined manner, making it easier for developers to integrate with the Somtoday API without directly handling the intricacies of the authentication process. The proxy also ensures that all necessary configurations and headers are appropriately managed, providing a robust solution for applications requiring Somtoday authentication.