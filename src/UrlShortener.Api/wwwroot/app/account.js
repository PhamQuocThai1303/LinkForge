const guestActions = document.querySelector('#guest-actions');
const userActions = document.querySelector('#user-actions');
const accountName = document.querySelector('#account-name');
const accountAvatar = document.querySelector('#account-avatar');
const avatarInitials = document.querySelector('#avatar-initials');
const avatarImage = document.querySelector('#avatar-image');

function initials(name) {
  return name.trim().split(/\s+/).slice(-2).map(part => part[0]?.toUpperCase() || '').join('');
}

async function loadAccount() {
  try {
    const response = await fetch('/api/v1/auth/me', { cache: 'no-store' });
    if (response.status === 401) {
      if (document.body.dataset.requireAuth) location.replace('/app/login.html?next=links');
      return;
    }
    if (!response.ok) throw new Error('Account request failed');
    const user = await response.json();
    accountName.textContent = user.name;
    accountAvatar.setAttribute('aria-label', `Avatar của ${user.name}`);
    avatarInitials.textContent = initials(user.name) || '?';
    if (user.avatarUrl) {
      const avatarUrl = new URL(user.avatarUrl);
      if (avatarUrl.protocol === 'https:' &&
          (avatarUrl.hostname === 'googleusercontent.com' || avatarUrl.hostname.endsWith('.googleusercontent.com'))) {
        avatarImage.src = avatarUrl.href;
        avatarImage.hidden = false;
        avatarImage.onerror = () => { avatarImage.hidden = true; };
      }
    }
    guestActions.hidden = true;
    userActions.hidden = false;
    document.body.classList.add('signed-in');
    document.dispatchEvent(new CustomEvent('account-ready', { detail: user }));
  } catch {
    document.dispatchEvent(new Event('account-error'));
  }
}

document.querySelector('#logout-button').addEventListener('click', async () => {
  const button = document.querySelector('#logout-button');
  button.disabled = true;
  try {
    const csrf = await fetch('/api/v1/auth/csrf', { cache: 'no-store' });
    if (!csrf.ok) throw new Error('CSRF request failed');
    const { token } = await csrf.json();
    const response = await fetch('/api/v1/auth/logout', {
      method: 'POST', headers: { 'X-CSRF-TOKEN': token }
    });
    if (!response.ok) throw new Error('Logout failed');
    if (document.body.dataset.requireAuth) {
      location.replace('/app/');
      return;
    }
    userActions.hidden = true;
    guestActions.hidden = false;
    accountName.textContent = '';
    avatarImage.hidden = true;
    document.body.classList.remove('signed-in');
  } catch {
    const status = document.querySelector('#account-status');
    if (status) status.textContent = 'Không thể đăng xuất lúc này. Vui lòng thử lại.';
  } finally {
    button.disabled = false;
  }
});

loadAccount();
