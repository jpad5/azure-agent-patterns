// =============================================================================
// Pattern 3 — Direct Line Web Chat Client
// =============================================================================

// ---------------------------------------------------------------------------
// 1. MSAL Configuration — Update placeholders before running
// ---------------------------------------------------------------------------
const msalConfig = {
    auth: {
        clientId: '<WEBCLIENT_APP_ID>',
        authority: 'https://login.microsoftonline.com/<TENANT_ID>',
        redirectUri: window.location.origin
    },
    cache: {
        cacheLocation: 'sessionStorage',
        storeAuthStateInCookie: false
    }
};

const msalInstance = new msal.PublicClientApplication(msalConfig);

const botTokenEndpoint = 'http://localhost:5040/api/directline/token';
const botAppScope = 'api://<BOT_APP_ID>/access_as_user';

// ---------------------------------------------------------------------------
// 2. DOM References
// ---------------------------------------------------------------------------
const signinBtn = document.getElementById('signin-btn');
const userDisplay = document.getElementById('user-display');
const backchannelToggle = document.getElementById('backchannel-toggle');

// ---------------------------------------------------------------------------
// 3. MSAL Sign-In
// ---------------------------------------------------------------------------
signinBtn.addEventListener('click', async () => {
    try {
        const loginResponse = await msalInstance.loginPopup({
            scopes: ['openid', 'profile', botAppScope]
        });
        userDisplay.textContent = `Signed in as ${loginResponse.account.username}`;
        signinBtn.style.display = 'none';
    } catch (err) {
        console.error('Sign-in failed:', err);
    }
});

// ---------------------------------------------------------------------------
// 4. Direct Line Token Exchange
// ---------------------------------------------------------------------------
async function getDirectLineToken() {
    const response = await fetch(botTokenEndpoint, { method: 'POST' });
    if (!response.ok) {
        throw new Error(`Token endpoint returned ${response.status}`);
    }
    const data = await response.json();
    return data.token;
}

// ---------------------------------------------------------------------------
// 5. Send User Token via Backchannel
// ---------------------------------------------------------------------------
async function sendBackchannelToken(directLine) {
    const accounts = msalInstance.getAllAccounts();
    if (accounts.length === 0) {
        console.warn('No signed-in account — cannot send backchannel token.');
        return;
    }

    try {
        const tokenResponse = await msalInstance.acquireTokenSilent({
            scopes: [botAppScope],
            account: accounts[0]
        });

        directLine.postActivity({
            type: 'event',
            name: 'userToken',
            value: tokenResponse.accessToken,
            from: { id: 'user' }
        }).subscribe(
            id => console.log(`Backchannel token sent (activity id: ${id})`),
            err => console.error('Failed to send backchannel token:', err)
        );
    } catch (err) {
        console.error('Failed to acquire token silently for backchannel:', err);
    }
}

// ---------------------------------------------------------------------------
// 6. Initialize Web Chat
// ---------------------------------------------------------------------------
async function initChat() {
    try {
        const token = await getDirectLineToken();
        const directLine = window.WebChat.createDirectLine({ token });

        const styleOptions = {
            rootHeight: '100%',
            rootWidth: '100%',
            backgroundColor: '#f7f9fc',
            bubbleBackground: '#e8f0fe',
            bubbleFromUserBackground: '#0078d4',
            bubbleFromUserTextColor: '#ffffff',
            sendBoxBackground: '#ffffff',
            sendBoxTextColor: '#323130',
            suggestedActionBackground: '#0078d4',
            suggestedActionTextColor: '#ffffff',
            hideUploadButton: true
        };

        window.WebChat.renderWebChat(
            { directLine, styleOptions },
            document.getElementById('webchat')
        );

        // Send backchannel token if the toggle is checked and user is signed in.
        if (backchannelToggle.checked) {
            await sendBackchannelToken(directLine);
        }

        // Allow sending backchannel token after chat is initialized.
        backchannelToggle.addEventListener('change', async () => {
            if (backchannelToggle.checked) {
                await sendBackchannelToken(directLine);
            }
        });

        console.log('Web Chat initialized successfully.');
    } catch (err) {
        console.error('Failed to initialize Web Chat:', err);
        document.getElementById('webchat').innerHTML =
            '<div class="loading-message error">Failed to connect. Is the bot running at localhost:5040?</div>';
    }
}

// ---------------------------------------------------------------------------
// 7. Bootstrap
// ---------------------------------------------------------------------------
initChat();
