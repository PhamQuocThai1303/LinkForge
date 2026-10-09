const linksList = document.querySelector('#links-list');
const linksStatus = document.querySelector('#links-status');
const pagination = document.querySelector('#pagination');
let currentPage = 1;

function formatNumber(value) { return new Intl.NumberFormat('vi-VN').format(value); }
function formatDate(value) { return new Intl.DateTimeFormat('vi-VN', { dateStyle: 'medium' }).format(new Date(value)); }

async function csrfToken() {
  const response = await fetch('/api/v1/auth/csrf', { cache: 'no-store' });
  if (!response.ok) throw new Error('CSRF request failed');
  return (await response.json()).token;
}

function renderLink(link) {
  const card = document.createElement('article');
  card.className = 'link-card';
  card.innerHTML = `
    <div class="link-card-main"><a target="_blank" rel="noopener noreferrer"></a><p class="link-original"></p></div>
    <div class="link-meta"><div><strong class="click-count"></strong><span>lượt click</span></div><time></time></div>
    <div class="link-actions"><button type="button" class="edit-link">Sửa short URL</button><button type="button" class="delete-link">Xóa link</button></div>
    <form class="edit-form" hidden><label>Alias mới</label><div class="edit-row"><span class="edit-prefix">/</span><input required minlength="3" maxlength="16" pattern="[A-Za-z0-9][A-Za-z0-9_\\-]{1,14}[A-Za-z0-9]" autocomplete="off" spellcheck="false"><button type="submit">Lưu thay đổi</button></div><p class="edit-hint">3–16 ký tự, gồm chữ, số, dấu gạch ngang hoặc gạch dưới. Mã 7 ký tự chữ và số được dành cho link tự sinh. Link cũ sẽ ngừng hoạt động.</p><p class="edit-error" role="alert" hidden></p></form>`;

  const shortLink = card.querySelector('.link-card-main a');
  shortLink.href = link.shortUrl;
  shortLink.textContent = link.shortUrl;
  const original = card.querySelector('.link-original');
  original.textContent = link.originalUrl;
  original.title = link.originalUrl;
  card.querySelector('.click-count').textContent = formatNumber(link.clickCount);
  const date = card.querySelector('time');
  date.dateTime = link.createdAt;
  date.textContent = formatDate(link.createdAt);

  const form = card.querySelector('.edit-form');
  const input = form.querySelector('input');
  const error = form.querySelector('.edit-error');
  const label = form.querySelector('label');
  input.id = `alias-${link.shortCode}`;
  label.htmlFor = input.id;
  input.value = link.shortCode;
  card.querySelector('.edit-link').addEventListener('click', () => {
    form.hidden = !form.hidden;
    if (!form.hidden) input.focus();
  });

  form.addEventListener('submit', async event => {
    event.preventDefault();
    error.hidden = true;
    const alias = input.value.trim();
    if (!/^[A-Za-z0-9][A-Za-z0-9_-]{1,14}[A-Za-z0-9]$/.test(alias) ||
        /^[A-Za-z0-9]{7}$/.test(alias)) {
      error.textContent = 'Alias cần 3–16 ký tự hợp lệ; mã 7 ký tự chữ và số được dành cho link tự sinh.';
      error.hidden = false;
      return;
    }
    const saveButton = form.querySelector('button');
    saveButton.disabled = true;
    try {
      const token = await csrfToken();
      const response = await fetch(`/api/v1/urls/${encodeURIComponent(link.shortCode)}`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': token },
        body: JSON.stringify({ shortCode: alias })
      });
      if (!response.ok) {
        error.textContent = response.status === 409 ? 'Alias này đã được sử dụng. Hãy chọn alias khác.'
          : response.status === 400 ? 'Alias không hợp lệ hoặc phiên đã hết hạn. Vui lòng thử lại.'
          : 'Không thể đổi alias lúc này. Vui lòng thử lại.';
        error.hidden = false;
        return;
      }
      await loadLinks();
      linksStatus.textContent = 'Đã cập nhật short URL.';
    } catch {
      error.textContent = 'Không kết nối được với máy chủ. Vui lòng thử lại.';
      error.hidden = false;
    } finally {
      saveButton.disabled = false;
    }
  });

  card.querySelector('.delete-link').addEventListener('click', async () => {
    if (!confirm(`Xóa link ${link.shortUrl}? Hành động này không thể hoàn tác.`)) return;
    try {
      const token = await csrfToken();
      const response = await fetch(`/api/v1/urls/${encodeURIComponent(link.shortCode)}`, {
        method: 'DELETE', headers: { 'X-CSRF-TOKEN': token }
      });
      if (!response.ok) throw new Error('Delete failed');
      await loadLinks();
      linksStatus.textContent = 'Đã xóa link.';
    } catch {
      linksStatus.textContent = 'Không thể xóa link lúc này. Vui lòng thử lại.';
    }
  });
  return card;
}

async function loadLinks() {
  linksStatus.textContent = 'Đang tải link...';
  try {
    const response = await fetch(`/api/v1/urls/mine?page=${currentPage}&pageSize=20`, { cache: 'no-store' });
    if (response.status === 401) { location.replace('/app/login.html?next=links'); return; }
    if (!response.ok) throw new Error('List request failed');
    const result = await response.json();
    if (!Array.isArray(result.links) || !Number.isInteger(result.total)) throw new Error('Invalid list response');
    if (currentPage > 1 && result.links.length === 0) { currentPage--; await loadLinks(); return; }
    linksList.replaceChildren(...result.links.map(renderLink));
    document.querySelector('#total-links').textContent = formatNumber(result.total);
    document.querySelector('#page-clicks').textContent = formatNumber(result.links.reduce((sum, link) => sum + link.clickCount, 0));
    const pageCount = Math.max(1, Math.ceil(result.total / result.pageSize));
    document.querySelector('#page-label').textContent = result.total ? `${result.total} link` : '';
    document.querySelector('#pagination-label').textContent = `${currentPage} / ${pageCount}`;
    document.querySelector('#previous-page').disabled = currentPage === 1;
    document.querySelector('#next-page').disabled = currentPage >= pageCount;
    pagination.hidden = result.total <= result.pageSize;
    linksStatus.textContent = result.total ? '' : 'Bạn chưa có link nào. Hãy tạo link đầu tiên.';
  } catch {
    linksStatus.textContent = 'Không tải được danh sách link. Vui lòng tải lại trang.';
  }
}

document.querySelector('#previous-page').addEventListener('click', () => { currentPage--; loadLinks(); });
document.querySelector('#next-page').addEventListener('click', () => { currentPage++; loadLinks(); });
document.addEventListener('account-ready', loadLinks);
document.addEventListener('account-error', () => {
  linksStatus.textContent = 'Không kiểm tra được tài khoản. Vui lòng tải lại trang.';
});
