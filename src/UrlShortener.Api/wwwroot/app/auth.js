const mode = document.body.dataset.authMode;
const form = document.querySelector('#auth-form');
const error = document.querySelector('#auth-error');
const submit = document.querySelector('#auth-submit');
const google = document.querySelector('#google-login');
const providerNote = document.querySelector('#provider-note');

function showError(message) {
  error.textContent = message;
  error.hidden = !message;
}

async function loadProvider() {
  try {
    const response = await fetch('/api/v1/auth/providers', { cache: 'no-store' });
    if (!response.ok) throw new Error('Provider request failed');
    const providers = await response.json();
    if (providers.google) return;
  } catch {
    // Keep the link disabled when the server's provider state cannot be read.
  }
  google.removeAttribute('href');
  google.setAttribute('aria-disabled', 'true');
  providerNote.textContent = 'Google sẽ khả dụng sau khi ứng dụng được cấu hình OAuth.';
  providerNote.hidden = false;
}

loadProvider();

const params = new URLSearchParams(location.search);
if (params.get('error') === 'account-exists') {
  showError('Email này đã có tài khoản. Hãy đăng nhập bằng email và mật khẩu.');
} else if (params.get('error') === 'google-failed') {
  showError('Không thể đăng nhập bằng Google. Vui lòng thử lại.');
}

document.querySelector('.reveal-button').addEventListener('click', (event) => {
  const password = document.querySelector('#password');
  const showing = password.type === 'password';
  password.type = showing ? 'text' : 'password';
  event.currentTarget.textContent = showing ? 'Ẩn' : 'Hiện';
  event.currentTarget.setAttribute('aria-label', showing ? 'Ẩn mật khẩu' : 'Hiện mật khẩu');
  event.currentTarget.setAttribute('aria-pressed', String(showing));
});

form.addEventListener('submit', async (event) => {
  event.preventDefault();
  showError('');

  const email = document.querySelector('#email').value.trim();
  const password = document.querySelector('#password').value;
  const name = mode === 'signup' ? document.querySelector('#name').value.trim() : null;
  if (mode === 'signup' && (name.length < 2 || name.length > 200)) {
    showError('Tên cần từ 2 đến 200 ký tự.');
    document.querySelector('#name').focus();
    return;
  }
  const emailInput = document.querySelector('#email');
  emailInput.value = email;
  if (!emailInput.validity.valid || !email) {
    showError('Nhập địa chỉ email hợp lệ.');
    emailInput.focus();
    return;
  }
  if ((mode === 'signup' && (password.length < 12 || password.length > 128)) || !password) {
    showError(mode === 'signup' ? 'Mật khẩu cần từ 12 đến 128 ký tự.' : 'Nhập mật khẩu của bạn.');
    document.querySelector('#password').focus();
    return;
  }

  submit.disabled = true;
  form.setAttribute('aria-busy', 'true');
  try {
    const csrfResponse = await fetch('/api/v1/auth/csrf', { cache: 'no-store' });
    if (!csrfResponse.ok) throw new Error('CSRF request failed');
    const { token } = await csrfResponse.json();
    const response = await fetch(`/api/v1/auth/${mode}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': token },
      body: JSON.stringify(mode === 'signup' ? { name, email, password } : { email, password })
    });
    if (response.ok) {
      location.replace(params.get('next') === 'links' ? '/app/links.html' : '/app/');
      return;
    }
    showError(response.status === 409 ? 'Email này đã được đăng ký. Hãy đăng nhập.'
      : response.status === 401 ? 'Email hoặc mật khẩu chưa đúng.'
      : response.status === 400 ? 'Thông tin chưa hợp lệ. Vui lòng kiểm tra lại.'
      : 'Không thể tiếp tục lúc này. Vui lòng thử lại.');
  } catch {
    showError('Không kết nối được với máy chủ. Vui lòng thử lại.');
  } finally {
    submit.disabled = false;
    form.removeAttribute('aria-busy');
  }
});
