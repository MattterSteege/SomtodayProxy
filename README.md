## Overzicht

**SomToday App Proxy (STAP)** is een proxyserver die is ontworpen om authenticatieverzoeken naar Somtoday te faciliteren en de ontvangen tokens te onderscheppen en door te sturen naar een opgegeven callback-URL. Dit project is ontstaan uit de behoefte aan een betere manier om toegangstokens en refresh tokens te verkrijgen van Somtoday, aangezien er tot nu toe geen effectieve methode bestond om dit te doen zonder de officiële procedures te volgen.

Het probleem waarmee ik een veel andere tegen aan liepen, was dat sommige gebruikers inlogden via Microsoft of andere third-party authenticators, waardoor het moeilijk was om direct toegang te krijgen tot de Somtoday tokens. STAP biedt een oplossing door deze authenticatiestap te omzeilen, waardoor ontwikkelaars [illegaal](https://som.today/nieuwsartikel/scholier-quinten-bouwt-mee-aan-nieuwe-leerling-en-ouderomgeving-ik-weet-hoe-leerlingen-denken/) kunnen hun gebruikers kunnen inloggen via Somtoday en de Somtoday tokens kunnen krijgen.

Dit project stelt ontwikkelaars in staat om deze tokens te onderscheppen door simpelweg verzoeken via de proxy te sturen. STAP vangt de authenticatierespons van Somtoday op en stuurt de tokens naar de door de gebruiker/ontwikkelaar opgegeven callback-URL. Dit proces maakt het mogelijk om toegang te krijgen tot de gegevens van Somtoday met minimale inspanning.

Ook Zorgt deze proxy ervoor dat de data van jouw gebruikers veilig blijft. Het stuurt een code terug die je direct bij Somtoday's API kunt inwisselen voor een access- en refresh_token. Als deze code niet werkt, dan weet je dat er gerommeld is in de proxy, werkt de code wel, dan kan (mocht iemand die code óók opgeslagen hebben) die code niet meer gebruiken.

Ja er is een door mij gehoste versie op https://somtoday.kronk.tech, die gebruikt mag worden, maar ik kan 100% uptime niet garanderen én misbruik wordt niet gewaardeerd.
## Belangrijk

De onderliggende gedacht van dit project is te danken aan Micha ([Micha.ga](https://micha.ga), [/FurriousFox](https://github.com/FurriousFox)). Dus dankjewel Micha voor het vinden van deze manier om mensen the authenticaten met SomToday!

## API endpoints

#### welkomspagina `GET /`

Toont een welkomspagina met basisinformatie over de proxy en een link naar de documentatie.

#### Vraag een request URL aan `GET /requestUrl`

Vraagt een unieke login-URL aan voor een gebruiker. De volgende queryparameters zijn vereist

| Parameter   | Type   | Description                                                                                                                                                                |
|-------------|--------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| user        | string | Een identificator voor de gebruiker die geauthenticeerd moet worden. Deze waarde wordt niet gebruikt door STAP, maar wordt teruggestuurd om de gebruiker te identificeren. |
| callbackUrl | string | De URL waarnaar de authenticatie token wordt verzonden als een POST-verzoek met een JSON-body.                                                                             |

#### Docs `GET /docs`

Redirect naar de GitHub-repository waar de uitgebreide documentatie te vinden is.

#### Proxy Requests `GET /*` (Waar * niet /docs of /requestUrl is)

Alle andere verzoeken worden doorgestuurd naar de SomToday API of het SomToday inlogsysteem, afhankelijk van het pad. Voorbeelden:

- Verzoeken met /rest/ worden doorgestuurd naar api.somtoday.nl.
- Verzoeken met /oauth2/authorize worden doorgestuurd naar inloggen.somtoday.nl.

#### Token Requests `POST /*` (Waar * niet /docs of /requestUrl is)
Verzoeken aan `/oauth2/token` worden gebruikt om tokens te onderscheppen en door te sturen naar de opgegeven callbackUrl.

#### callback data `POST <callbackUrl>`

Hieronder is een voorbeeld van de data die naar je toe wordt gestuurd als de callback getriggerd wordt. Deze data komt als `application/json` in de body:

```json
    {
    "grant_type": "authorization_code",
    "code": "[you specifieke auth code]",
    "redirec\nt_uri": "somtoday://nl.topicus.somtoday.leerling/oauth/callback",
    "code_verifier": "[code verifier]",
    "client_id": "somtoday-leerling-native",
    "claims": "{\"id_token\":{\"given_name\":null, \"leerlingen\":null, \"orgname\": null, \"affiliation\":{\"values\":[\"student\",\"parent/guardian\"]} }}"
    }
```

Kijk onder "Voorbeeld > Callback data opvangen" wat je verder moet doen.


## Voorbeeld

Hier wordt een stappenplan uitgelegd, hoe je deze authenticatie methode gebruikt in jouw applicatie:

### 1. Vraag een URL aan

Een nieuwe login sessie aanmaken is super makkelijk, je gaat naar `GET /requestUrl?user=janSmit&callbackUrl=janSmit.com/callback` (met eigen data) je krijgt dan een JSON object terug, dat ziet er zo uit: 

```json
{
  "user": "janSmit", //door jou gezet
  "vanityUrl": "somtoday.kronk.tech/1234", //1234 is een random 4 cijferige code
  "expires": "2024-08-13T17:50:59.0933886+00:00", //zolang deze sessie geldig is
  "callbackUrl": "janSmit.com/callback" //door jou gezet
}
```


### 2. Eindgebruiker is aan zet

De eindgebruiker moet **op een mobiel device** (voor nu werkt het via een browser nog niet) de nieuwe Somtoday app openen (V2.0.0 en hoger).

Als de gebruiker ingelogd is moet hij uitloggen.

Nu zie je de splashscreen van Somtoday, als je hier bent, klik dan 5x op het Somtoday logo bovenin. Als dat gebeurd is, dan zie je een lijst met opties (bijv. "nighlty", "productie" & "test") hier selecteerd de gebruiker "ontwikkel".

Dan vul je in het invulveld onderin ("hostname of ip indien niet localhost") de url in die is gegeven bij je vorige request `vanityUrl`, je neemt deze letterlijk over, als dit niet gebeurt dan werkt de methode niet. 

- https://somtoday.kronk.tech/1234 = fout
- somtoday.kronk.tech/1234/ = fout
- stomtoday.kronk.tech/1234 = fout
- somtoday.kronk.tech/1234 = goed

als dit goed gaat, dan opent automatisch de browser van de gebruiker (of is dat nog gecached en gaat de gebruiker gelijk naar de **laatste stap**)

Nu de browser is geopend met de **echte** inlogpagina van Somtoday moet de gebruiker inloggen zoals normaal.

(*laatste stap*) Als de gebruiker op "Inloggen" klikt, dan wordt de gebruiker terug gestuurd naar de Somtoday app en wordt de token onderschept en terug gestuurd naar jouw, de ontwikkelaar (via de callbackUrl)

### Callback data opvangen
```javascript
const express = require('express');
const axios = require('axios');
const bodyParser = require('body-parser');

const app = express();
app.use(bodyParser.json());

app.post('/callback', async (req, res) => {
    const model = req.body;
    
    if (!model) {
        return res.status(200).send("failed");
    }

    try {
        const response = await axios.post('https://inloggen.somtoday.nl/oauth2/token', new URLSearchParams({
            grant_type: model.grant_type,
            code: model.code,
            redirect_uri: model.redirect_uri,
            code_verifier: model.code_verifier,
            client_id: model.client_id,
            claims: model.claims,
        }), {
            headers: {
                'accept': 'application/json, text/plain, */*',
                'origin': 'https://leerling.somtoday.nl'
            }
        });

        const somtodayAuthentication = response.data;

        console.log(somtodayAuthentication)

        return res.status(200).send("Danku"); // er wordt niks gedaan met wat je terug stuurt :)
    } catch (error) {
        console.error('Error during LegitAuth:', error);
        return res.status(500).send("failed");
    }
});

// Start the server
const port = 3000;
app.listen(port, () => {
    console.log(`Server is running on port ${port}`);
});
```

### Profijt

Je hebt nu een originele Somtoday token én refresh token die 60 dagen geldig blijft, zolagn je binnen deze 60 dagen een nieuwe token aanvraagd blijft deze voor (zo ver ik weet voor) eeuwig geldig.

Ben je benieuwd hoe je deze token kunt gebruiken, kijk dan op de onoficiele docs [/elisaado/somtoday-api-docs](https://github.com/elisaado/somtoday-api-docs), waar ik overigens maintainer van ben :)
### Installeren
To install the ASP.NET Core Dynamic Page Loading System, follow these steps:

1. Clone de repository naar je lokale machine:

   ```bash
    git clone https://github.com/matttersteege/SomtodayProxy.git
   ```

2. Navigeer naar de map en installeer de vereiste dependencies:

   ```bash
    cd SomtodayProxy
    dotnet restore
   ```

3. Bouw het project:

   ```bash
   dotnet build
   ```
## Hosten

Publiceer de applicatie:
```bash
dotnet publish -c Release -o ./publish
```
Host de bestanden op een webserver zoals Nginx of IIS. Zorg ervoor dat de server is geconfigureerd om HTTPS-verzoeken om te leiden en gebruik een reverse proxy indien nodig.
## Auteurs

- [@Kronk](https://www.github.com/matttersteege) - Ontwikkelaar van deze repo en deels verbeteren van originele idee
- [@Micha](https://github.com/luxkatana) - Oorspronklijk vinder van deze autheticatie methode
