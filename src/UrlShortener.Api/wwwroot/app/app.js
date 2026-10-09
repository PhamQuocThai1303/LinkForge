const form = document.querySelector('#shorten-form');
const urlInput = document.querySelector('#original-url');
const errorMessage = document.querySelector('#form-error');
const submitButton = document.querySelector('#submit-button');
const submitLabel = document.querySelector('#submit-label');
const emptyState = document.querySelector('#empty-state');
const resultState = document.querySelector('#result-state');
const shortUrl = document.querySelector('#short-url');
const originalUrl = document.querySelector('#result-original');
const managementToken = document.querySelector('#management-token');
const copyStatus = document.querySelector('#copy-status');
const createStatus = document.querySelector('#create-status');

function showError(message) {
  errorMessage.textContent = message;
  errorMessage.hidden = !message;
  urlInput.setAttribute('aria-invalid', message ? 'true' : 'false');
}

function validateUrl(value) {
  if (!value) return 'Nhập URL bạn muốn rút gọn.';
  if (value.length > 2048) return 'URL phải có tối đa 2.048 ký tự.';

  try {
    const parsed = new URL(value);
    if (!['http:', 'https:'].includes(parsed.protocol) || !parsed.hostname || parsed.username || parsed.password) {
      return 'Nhập URL HTTP hoặc HTTPS hợp lệ, không chứa thông tin đăng nhập.';
    }
  } catch {
    return 'Nhập URL đầy đủ, bắt đầu bằng https:// hoặc http://.';
  }

  return '';
}

form.addEventListener('submit', async (event) => {
  event.preventDefault();
  const value = urlInput.value.trim();
  const validationError = validateUrl(value);
  showError(validationError);
  if (validationError) {
    urlInput.focus();
    return;
  }

  submitButton.disabled = true;
  submitLabel.textContent = 'Đang tạo link...';
  form.setAttribute('aria-busy', 'true');
  copyStatus.textContent = '';
  createStatus.textContent = '';

  try {
    const response = await fetch('/api/v1/urls', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ url: value })
    });

    if (!response.ok) {
      showError(response.status === 400
        ? 'URL chưa hợp lệ. Hãy dùng địa chỉ HTTP(S) công khai, không có IP, localhost hoặc thông tin đăng nhập.'
        : 'Không thể tạo link lúc này. Vui lòng thử lại.');
      return;
    }

    const created = await response.json();
    const parsedShortUrl = new URL(created.shortUrl);
    if (!['http:', 'https:'].includes(parsedShortUrl.protocol) || !created.managementToken) {
      throw new Error('Invalid create response');
    }

    shortUrl.href = parsedShortUrl.href;
    shortUrl.textContent = created.shortUrl;
    originalUrl.textContent = value;
    originalUrl.title = value;
    managementToken.textContent = created.managementToken;
    emptyState.hidden = true;
    resultState.hidden = false;
    showError('');
    createStatus.textContent = 'Link ngắn đã được tạo. Kết quả nằm trong phần Kết quả của bạn.';
  } catch {
    showError('Không kết nối được với máy chủ. Kiểm tra kết nối rồi thử lại.');
  } finally {
    submitButton.disabled = false;
    submitLabel.textContent = 'Rút gọn URL';
    form.removeAttribute('aria-busy');
  }
});

urlInput.addEventListener('input', () => {
  if (!errorMessage.hidden) showError('');
});

async function copyText(value) {
  if (navigator.clipboard?.writeText) {
    await navigator.clipboard.writeText(value);
    return;
  }

  const temporary = document.createElement('textarea');
  temporary.value = value;
  temporary.style.position = 'fixed';
  temporary.style.opacity = '0';
  document.body.append(temporary);
  temporary.select();
  const copied = document.execCommand('copy');
  temporary.remove();
  if (!copied) throw new Error('Copy failed');
}

document.querySelector('#copy-url').addEventListener('click', async () => {
  try {
    await copyText(shortUrl.textContent);
    copyStatus.textContent = 'Đã sao chép link ngắn.';
  } catch {
    copyStatus.textContent = 'Không thể sao chép tự động. Hãy chọn và sao chép link ở trên.';
  }
});

document.querySelector('#copy-token').addEventListener('click', async () => {
  try {
    await copyText(managementToken.textContent);
    copyStatus.textContent = 'Đã sao chép mã quản lý.';
  } catch {
    copyStatus.textContent = 'Không thể sao chép tự động. Hãy chọn và sao chép mã ở trên.';
  }
});
