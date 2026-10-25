'use strict';
const setupForm = document.querySelector('#setup-form');
setupForm.addEventListener('change', () => {
  const data = new FormData(setupForm);
  document.querySelector('#selection-summary').textContent =
    (data.get('players') === '2' ? '兩人 · 俠客 × 琴師' : '一人 · 俠客') +
    ' · ' + ({gesture: '手勢動作', mapping: '手部映射', hybrid: '混合操控'}[data.get('mode')]);
});
