// ======================================================
// GERENCIADOR DE POWER-UPS - TOTALMENTE INDEPENDENTE
// ======================================================

// Este objeto encapsula toda a lógica de power-ups.
const PowerUpManager = {
    // Propriedades
    powerups: [],
    rallyCount: 0,

    // Constantes
    POWERUP_LIFETIME: 8000, // 8 segundos
    SPAWN_RALLY_COUNT: 4,
    powerUpTypes: [
        { type: 'widePaddle', icon: '↔️', for: 'self' },
        { type: 'fastBall', icon: '⚡', for: 'ball' },
        { type: 'invisibleBall', icon: '👻', for: 'ball' }
    ],

    // Referências a objetos do jogo principal (serão injetadas)
    ctx: null,
    ball: null,
    paddle1: null,
    paddle2: null,
    PADDLE_WIDTH_NORMAL: 80,
    PADDLE_WIDTH_WIDE: 120,
    BALL_SPEED_NORMAL: 5,
    BALL_SPEED_FAST: 8,

    // Método de inicialização para receber os objetos do jogo
    initialize(context, gameBall, player1, player2, constants) {
        this.ctx = context;
        this.ball = gameBall;
        this.paddle1 = player1;
        this.paddle2 = player2;
        this.PADDLE_WIDTH_NORMAL = constants.PADDLE_WIDTH_NORMAL;
        this.PADDLE_WIDTH_WIDE = constants.PADDLE_WIDTH_WIDE;
        this.BALL_SPEED_NORMAL = constants.BALL_SPEED_NORMAL;
        this.BALL_SPEED_FAST = constants.BALL_SPEED_FAST;
    },

    // Método para ser chamado a cada quadro do jogo
    update() {
        if (!this.ball) return; // Não faz nada se o jogo não começou

        // Gerencia o spawn de novos power-ups
        if (this.rallyCount >= this.SPAWN_RALLY_COUNT && this.powerups.length === 0) {
            this.spawn();
        }

        // Atualiza os power-ups existentes
        this.powerups.forEach((p, index) => {
            if (Date.now() - p.createdAt > this.POWERUP_LIFETIME) {
                this.powerups.splice(index, 1);
                return;
            }
            if (Math.hypot(this.ball.x - p.x, this.ball.y - p.y) < this.ball.radius + 15) {
                this.activate(p, this.ball.lastHitBy);
                this.powerups.splice(index, 1);
            }
        });
    },

    spawn() {
        const typeInfo = this.powerUpTypes[Math.floor(Math.random() * this.powerUpTypes.length)];
        this.powerups.push({
            x: Math.random() * (600 - 100) + 50, // Usa 600 como largura do canvas
            y: Math.random() * (450 / 2) + (450 / 4), // Usa 450 como altura
            type: typeInfo.type,
            icon: typeInfo.icon,
            for: typeInfo.for,
            createdAt: Date.now()
        });
        this.rallyCount = 0;
    },

    activate(powerup, activator) {
        const targetPaddle = activator === 'player1' ? this.paddle1 : this.paddle2;
        switch (powerup.type) {
            case 'widePaddle':
                targetPaddle.width = this.PADDLE_WIDTH_WIDE;
                setTimeout(() => { if (targetPaddle) targetPaddle.width = this.PADDLE_WIDTH_NORMAL; }, 8000);
                break;
            case 'fastBall':
                this.ball.isFast = true;
                this.normalizeBallSpeed(this.BALL_SPEED_FAST);
                setTimeout(() => {
                    this.ball.isFast = false;
                    this.normalizeBallSpeed(this.BALL_SPEED_NORMAL);
                }, 6000);
                break;
            case 'invisibleBall':
                this.ball.isInvisible = true;
                setTimeout(() => { this.ball.isInvisible = false; }, 3000);
                break;
        }
    },

    // Desenha todos os power-ups na tela
    draw() {
        if (!this.ctx) return;
        this.powerups.forEach(p => {
            this.ctx.font = '24px Arial';
            this.ctx.textAlign = 'center';
            this.ctx.textBaseline = 'middle';
            this.ctx.fillText(p.icon, p.x, p.y);
        });
    },

    // Zera o estado dos power-ups
    reset() {
        this.powerups = [];
        this.rallyCount = 0;
    },

    // Incrementa o contador de rebatidas
    incrementRally() {
        this.rallyCount++;
    },

    normalizeBallSpeed(speed) {
        const currentSpeed = Math.sqrt(this.ball.dx * this.ball.dx + this.ball.dy * this.ball.dy);
        if (currentSpeed > 0) {
            this.ball.dx = (this.ball.dx / currentSpeed) * speed;
            this.ball.dy = (this.ball.dy / currentSpeed) * speed;
        }
    }
};